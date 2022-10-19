using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using MonoMod.Utils;
using TransformChangesDebugger.API.Extensions;
using TransformChangesDebugger.API.Utilities;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = System.Object;

namespace TransformChangesDebugger.API.Patches
{
    /// <summary>
    /// Responsible for finding and patching methods that modify transforms
    /// </summary>
    public class TransformPatches
    {
        public static event EventHandler<RedirectSetterMethodsFromCallingCodeResult> InterceptMethodsToEnableChangeTrackingCompleted; 
        public static event EventHandler InterceptMethodsToEnableChangeTrackingStarted; 
        
        public static Dictionary<MethodToPathCacheKey, CachedAssemblyWithMethodsToPatchInfo> AssemblyMethodsToPatchCache = new Dictionary<MethodToPathCacheKey, CachedAssemblyWithMethodsToPatchInfo>();
        public static HashSet<string> AlreadyPatchedAssyFilePaths = new HashSet<string>();
        
        private static MethodInfo ToStringMethodReference = typeof(Object).GetMethod("ToString");
        private static MethodInfo StringEqualsMethodReference = typeof(String).GetMethod("op_Equality");

        public static RedirectSetterMethodsFromCallingCodeResult InterceptMethodsToEnableChangeTracking(Harmony harmony, List<FileInfo> assemblyPaths)
        {
#if !NET_4_6
    Debug.LogWarning("TransformChangesDebugger.API: Unable to start tracking changes - interception can only be set up on .NET 4.x backend, go to player settings to change");
#endif
            
            RedirectSetterMethodsFromCallingCodeResult result = null;
            var startEventFired = false;
            try
            {
                var totalSw = new Stopwatch();
                totalSw.Start();

                var methodInterceptionParams = new List<MethodInterceptionParams>();
                var assemblyToMethodInterceptionParams =
                    new Dictionary<FileInfo, RedirectSetterMethodsFromCallingCodeForAssyResult>();

                var notYetPatchedAssyFiles = assemblyPaths
                    .Where(assemblyPath => !AlreadyPatchedAssyFilePaths.Contains(assemblyPath.FullName)).ToList();

                if (!notYetPatchedAssyFiles.Any())
                {
                    return new RedirectSetterMethodsFromCallingCodeResult(
                        new List<RedirectSetterMethodsFromCallingCodeForAssyResult>(), totalSw.ElapsedMilliseconds);
                }

                InterceptMethodsToEnableChangeTrackingStarted?.Invoke(null, EventArgs.Empty);
                startEventFired = true;

                //Read all types upfront so we're sure they are loaded in to memory and can be patched
                using (var unityModule = ModuleDefinition.ReadModule(typeof(Transform).Module.FullyQualifiedName))
                    foreach (var assemblyPath in notYetPatchedAssyFiles)
                    {
                        var timeTakenToFindMethodsSw = new Stopwatch();
                        timeTakenToFindMethodsSw.Start();

                        var findMethodsWithInstructionsToInterceptResults =
                            FindMethodsWithInstructionsToIntercept(assemblyPath, unityModule);
                        var methodInterceptionParamsForSingleAssy = findMethodsWithInstructionsToInterceptResults
                            .SelectMany(r => r.MethodInterceptionParams)
                            .ToList();
                        //That's a bit of a simplification, potentially some of the entries can be cache and some not in same assy, that really will happen very rarely, simplified to treat as all from cache if any is from cache
                        var isAnyFindSettersToPatchResolvedFromCache = findMethodsWithInstructionsToInterceptResults
                            .Where(r => r.MethodToPathCacheKey.AssemblyFullPath == assemblyPath.FullName)
                            .Any(r => r.IsFromCache);
                        assemblyToMethodInterceptionParams.Add(assemblyPath,
                            new RedirectSetterMethodsFromCallingCodeForAssyResult(assemblyPath,
                                timeTakenToFindMethodsSw.ElapsedMilliseconds,
                                methodInterceptionParamsForSingleAssy,
                                isAnyFindSettersToPatchResolvedFromCache
                            ));
                    }


                foreach (var assemblyPath in notYetPatchedAssyFiles)
                {
                    var methodInterceptionParamsForSingleAssyWithTimeTakenToFindMethodsPair =
                        assemblyToMethodInterceptionParams[assemblyPath];
                    var methodInterceptionParamsForSingleAssy =
                        methodInterceptionParamsForSingleAssyWithTimeTakenToFindMethodsPair
                            .MethodInterceptionParamEntries;
                    if (methodInterceptionParamsForSingleAssy.Any())
                    {
                        methodInterceptionParams.AddRange(methodInterceptionParamsForSingleAssy);

                        var perMethodPatchingDurations = new Dictionary<MethodInterceptionParams, long>();

                        var timeTakenToPatchMethodsSw = new Stopwatch();
                        timeTakenToPatchMethodsSw.Start();
                        foreach (var methodInterceptorParam in methodInterceptionParamsForSingleAssy)
                        {
                            var timeTakenToPatchMethodsSingleMethodSw = new Stopwatch();
                            timeTakenToPatchMethodsSingleMethodSw.Start();

                            var interceptCallParamsForType =
                                TranspiledMethodDefinitions.InterceptionTypeToInterceptCallParameters[
                                    methodInterceptorParam.PatchingDueToInterceptedMethodCallFullName];
                            //PERF: patching can take a while, especially for bigger assy like UnityEngine.Core - how to get that speed up?
                            harmony.Patch(methodInterceptorParam.MethodDefinition.ResolveReflection(),
                                transpiler: interceptCallParamsForType.Transpiler);

                            perMethodPatchingDurations.Add(methodInterceptorParam,
                                timeTakenToPatchMethodsSingleMethodSw.ElapsedMilliseconds);
                        }

                        methodInterceptionParamsForSingleAssyWithTimeTakenToFindMethodsPair.TimeTakenToPatchMethods =
                            timeTakenToPatchMethodsSw.ElapsedMilliseconds;
                        methodInterceptionParamsForSingleAssyWithTimeTakenToFindMethodsPair
                            .TimeTakenPerMethodInterceptionParamToPatchMethods = perMethodPatchingDurations;
                        methodInterceptionParamsForSingleAssyWithTimeTakenToFindMethodsPair.IsPatchAssemblyExecuted =
                            true;

                        AlreadyPatchedAssyFilePaths.Add(assemblyPath.FullName);
                    }
                }

                foreach (var alreadyPatchedPreviouslyAssyFile in assemblyPaths.Except(notYetPatchedAssyFiles))
                {
                    assemblyToMethodInterceptionParams.Add(alreadyPatchedPreviouslyAssyFile,
                        new RedirectSetterMethodsFromCallingCodeForAssyResult(
                            alreadyPatchedPreviouslyAssyFile,
                            0,
                            new List<MethodInterceptionParams>(),
                            false
                        )
                    );
                }

                result = new RedirectSetterMethodsFromCallingCodeResult(
                    assemblyToMethodInterceptionParams.Select(kv => kv.Value).ToList(),
                    totalSw.ElapsedMilliseconds
                );
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"Unable to patch assembles, {e.Message}");
                return null;
            }
            finally
            {
                if(startEventFired) InterceptMethodsToEnableChangeTrackingCompleted?.Invoke(null, result);
            }
        }
        
        
        private static List<FindMethodsWithInstructionsToInterceptResult> FindMethodsWithInstructionsToIntercept(FileInfo assemblyFile, ModuleDefinition unityModule)
        {
            List<MethodInterceptionParams> FindMethodsWithInstructionsCallingAndUpdateCache(ModuleDefinition rootModule, MethodDefinition methodToFind, string targetPatchMethodFullName, MethodToPathCacheKey methodToPathCacheKey)
            {
                var methodInterceptionParams = FindMethodsWithInstructionsCalling(rootModule, methodToFind, targetPatchMethodFullName)
                    .Where(m => m.MethodDefinition.CustomAttributes.All(ca =>
                        ca.AttributeType.Name != nameof(SkipTransformPropertySetterAutoPatchingAttribute)))
                    .ToList();

                AssemblyMethodsToPatchCache[methodToPathCacheKey] = new CachedAssemblyWithMethodsToPatchInfo(
                    assemblyFile.LastWriteTimeUtc, 
                    assemblyFile.FullName, 
                    methodInterceptionParams.Select(p => new TypeNameToFullNamePair(p.MethodDefinition.DeclaringType.FullName, p.MethodDefinition.FullName)).ToList()
                );
                
                return methodInterceptionParams;
            }

            var results = new List<FindMethodsWithInstructionsToInterceptResult>();
            using (var rootModule = ModuleDefinition.ReadModule(assemblyFile.FullName, new ReaderParameters() {ReadWrite = false}))
            {
                foreach (var kv in TranspiledMethodDefinitions.InterceptionTypeToInterceptCallParameters)
                {
                    var methodToFind = kv.Value.GetMethodToFind(unityModule);
                    var targetPatchMethodFullName = kv.Key;

                    var methodToPathCacheKey = new MethodToPathCacheKey(assemblyFile.FullName, targetPatchMethodFullName);
                    
                    if (AssemblyMethodsToPatchCache.TryGetValue(methodToPathCacheKey, out var cached))
                    {
                        //TODO: cache does not seem to resolve correctly, time is always 0000
                        if (Math.Abs((cached.CachedAssemblyFileCompilationTime - assemblyFile.LastWriteTimeUtc).TotalSeconds) > 5)
                        {
                            var methodInterceptorParams = FindMethodsWithInstructionsCallingAndUpdateCache(rootModule, methodToFind, targetPatchMethodFullName, methodToPathCacheKey);
                            results.Add(new FindMethodsWithInstructionsToInterceptResult(methodToPathCacheKey, false, methodInterceptorParams));
                        }
                        else
                        {
                            //resolving from cache needs to be serializable to strings as it'll not be persisted between sessions, couldn't find a good method to resolve properly from
                            //MethodDefinition.FullName, instead we'll lookup type which should be quick and then iterate over all methods for that type - still should be quite fast
                            var methodInterceptorParamsCreatedFromCache = new List<MethodInterceptionParams>();
                            foreach (var typeNameToFullNamePair in cached.TypeNameToFullMethodNamesPairToPatch)
                            {
                                var type = rootModule.GetType(typeNameToFullNamePair.TypeName);
                                var methodDefinitionToPatch = type.GetMethods().Concat(type.GetConstructors()).SingleOrDefault(m => m.FullName == typeNameToFullNamePair.FullMethodName);
                                if (methodDefinitionToPatch == null)
                                {
                                    Debug.LogWarning($"Unable find method to patch from cache '{typeNameToFullNamePair.FullMethodName}'");
                                }

                                else
                                    methodInterceptorParamsCreatedFromCache.Add(new MethodInterceptionParams(methodDefinitionToPatch, targetPatchMethodFullName));
                            }
                            
                            results.Add(new FindMethodsWithInstructionsToInterceptResult(methodToPathCacheKey, true, methodInterceptorParamsCreatedFromCache));
                        }
                    }
                    else
                    {
                        var methodInterceptorParams = FindMethodsWithInstructionsCallingAndUpdateCache(rootModule, methodToFind, targetPatchMethodFullName, methodToPathCacheKey);
                        results.Add(new FindMethodsWithInstructionsToInterceptResult(methodToPathCacheKey, false, methodInterceptorParams));
                    }
                }

                return results;
            }
        }
        
#if NET_4_6
        internal static IEnumerable<CodeInstruction> TranspileGenericSet(MethodBase methodBase, ILGenerator il, IEnumerable<CodeInstruction> instructions, string fullMethodNameToTrack)
        {
            var instructionsToReturn = new List<CodeInstruction>();

            void AddCodeInstruction(OpCode opcode, object operand = null)
            {
                instructionsToReturn.Add(new CodeInstruction(opcode, operand));
            }

            void AddCodeInstructions(IEnumerable<CodeInstruction> codeInstructions)
            {
                instructionsToReturn.AddRange(codeInstructions);
            }
            
            var instructionsAsList = (List<CodeInstruction>) instructions;
            for (var i = 0; i < instructionsAsList.Count; i++)
            {
                var instructionsInjectedBeforeCurrentInstructions = false;
                var instruction = instructionsAsList[i];
                LocalBuilder callingOnVariable = null;
                Label jumpLabelAfterSetter = new Label();
                if ((instruction.operand as MethodBase)?.ResolveFullName() == fullMethodNameToTrack)
                {
                    instructionsToReturn.Clear();
                    
                    var methodInfo = (MethodInfo)instruction.operand;
                    var methodCallParamsArrayVariable = il.DeclareLocal(typeof(object[]));
                    callingOnVariable = il.DeclareLocal(methodInfo.DeclaringType);
                    var arrayArgsVariable = il.DeclareLocal(typeof(object[]));
                    
                    //store existing stack values locally so they can be later used
                    var methodCallParameters = methodInfo.GetParameters();

                    var variablesForMethodCallParameters = new LocalBuilder[methodCallParameters.Length];
                    for (var index = methodCallParameters.Length - 1; index >= 0; index--) //take from stack from last to first so types are correct
                    {
                        var methodCallParameter = methodCallParameters[index];
                        //TODO: PERF: perhaps it'd be better to load directly to array and not go via local variables? the issue is then with reloading them back onto stack to be called
                        variablesForMethodCallParameters[index] = il.DeclareLocal(methodCallParameter.ParameterType); 
                        AddCodeInstruction(OpCodes.Stloc, variablesForMethodCallParameters[index]);
                    }

                    AddCodeInstruction(OpCodes.Stloc, callingOnVariable);
                    //at this stage stack should be clear from any loaded args, do custom work and then reload all of them back
                    
                    //create an array for all method call params, this will later be used to send down to SendMessage
                    AddCodeInstructions(
                        ILArrayGenerator.CreateArray(variablesForMethodCallParameters.Length, typeof(object))
                    );
                    for (var index = 0; index < variablesForMethodCallParameters.Length; index++)
                    {
                        var variableForMethodCallParameter = variablesForMethodCallParameters[index];
                        var arrayLoadInstructions = new List<CodeInstruction>()
                        {
                            new CodeInstruction(OpCodes.Ldloc, variableForMethodCallParameter)
                        };
                        var methodParameter = methodCallParameters[index];
                        if (methodParameter.ParameterType.IsValueType)
                        {
                            arrayLoadInstructions.Add(new CodeInstruction(OpCodes.Box, methodParameter.ParameterType));
                        }
                        
                        AddCodeInstructions(
                            ILArrayGenerator.PopulateArrayAtIndex(index, arrayLoadInstructions.ToArray())
                        );
                    }
                    
                    //store method call params array
                    AddCodeInstruction(OpCodes.Stloc, methodCallParamsArrayVariable);
                    
                    //create Args array
                    AddCodeInstructions(
                        ILArrayGenerator.CreateArrayWithValues(typeof(object), arrayArgsVariable, new List<List<CodeInstruction>>
                        {
                            new List<CodeInstruction>
                            {
                                new CodeInstruction(OpCodes.Ldarg_0) //'this' for calee
                            },
                            new List<CodeInstruction>
                            {
                                //calling method name
                                new CodeInstruction(OpCodes.Ldstr, methodBase.ResolveFullName()) 
                            },
                            new List<CodeInstruction>
                            {
                                //values array
                                new CodeInstruction(OpCodes.Ldloc, methodCallParamsArrayVariable),
                            },
                            new List<CodeInstruction>
                            {
                                //full tracked method name
                                new CodeInstruction(OpCodes.Ldstr, fullMethodNameToTrack) //second index, calling method name
                            },
                            //5th element empty as it's used for return value that's used to determine if setter should execute
                        }, 5)
                    );

                    //call Component.SendMessage() - first time
                    AddCodeInstruction(OpCodes.Ldloc, callingOnVariable);
                    AddCodeInstruction(OpCodes.Ldstr, CoreInterceptionPatch.InterceptorMethodNameForSendMessageBeforeOriginalExecution);
                    AddCodeInstruction(OpCodes.Ldloc, arrayArgsVariable);
                    AddCodeInstruction(OpCodes.Callvirt, CoreInterceptionPatch.SendMessageMethod);

                    //check if original method should be run by looking up 5th element of array that's now set with true/false
                    AddCodeInstruction(OpCodes.Ldloc, arrayArgsVariable);
                    AddCodeInstruction(OpCodes.Ldc_I4_4);
                    AddCodeInstruction(OpCodes.Ldelem_Ref);
                    
                    //HACK: this should just compare bool but unboxing in Linqpad is followed with stloc then ldloc immediately,
                    //quite odd and couldn't recreate, instead reverted to calling ToString and comparing result
                    AddCodeInstruction(OpCodes.Callvirt, ToStringMethodReference);
                    AddCodeInstruction(OpCodes.Ldstr, "False");
                    AddCodeInstruction(OpCodes.Callvirt, StringEqualsMethodReference);

                    //add label after setter to jump to if specified
                    jumpLabelAfterSetter = il.DefineLabel();
                    // if ShouldExecuteOriginalCall param set to true then move to next instruction after the one to inject before, effectively skip setter
                    AddCodeInstruction(OpCodes.Brtrue, jumpLabelAfterSetter);  

                    //re-add args on stack so original method can call it - this will be skipped on brFalse if should not be executed at all
                    AddCodeInstruction(OpCodes.Ldloc, callingOnVariable);
                    
                    for (var index = 0; index < variablesForMethodCallParameters.Length; index++)
                    {
                        var variableForMethodCallParameter = variablesForMethodCallParameters[index];
                        AddCodeInstruction(OpCodes.Ldloc, variableForMethodCallParameter);
                    }

                    foreach (var codeInstruction in instructionsToReturn)
                        yield return codeInstruction;

                    instructionsInjectedBeforeCurrentInstructions = true;
                }

                yield return instruction;

                //add more instructions after initial call
                if (instructionsInjectedBeforeCurrentInstructions)
                {
                    //call Component.SendMessage() - second time
                    yield return new CodeInstruction(OpCodes.Ldloc, callingOnVariable) {labels = new List<Label>() { jumpLabelAfterSetter }};
                    yield return new CodeInstruction(OpCodes.Ldstr, CoreInterceptionPatch.InterceptorMethodNameForSendMessageAfterOriginalExecution);
                    yield return new CodeInstruction(OpCodes.Ldnull);
                    yield return new CodeInstruction(OpCodes.Callvirt, CoreInterceptionPatch.SendMessageMethod);
                }
            }
        }
#else
            internal static IEnumerable<CodeInstruction> TranspileGenericSet(MethodBase methodBase, object il, IEnumerable<CodeInstruction> instructions, string fullMethodNameToTrack)
            {
                throw new Exception("Tool only supports API Compatibility level 4.x");
            }  
#endif
        
        //TODO PERF: for big assemblies iterating like that is taking ages, need to have some kind of cache or just iterate through assembly once and check all instructions there
        private static List<MethodInterceptionParams> FindMethodsWithInstructionsCalling(ModuleDefinition moduleDefinition, MethodDefinition callingToMethod, string targetPatchMethodFullName)
        {
            var methodWithInstructionsToReplace = new List<MethodInterceptionParams>();

            foreach (var t in moduleDefinition.Types.Where(t => !TranspiledMethodDefinitions.FullTypesNamesExcludedFromPatching.Contains(t.FullName)))
            {
                foreach (var method in t.Methods)
                {
                    if (method.Body != null)
                    {
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if ((instruction.Operand as MethodReference)?.FullName == callingToMethod.FullName)
                            {
                                methodWithInstructionsToReplace.Add(new MethodInterceptionParams(method, targetPatchMethodFullName));
                            }
                        }
                    }
                }
            }

            return methodWithInstructionsToReplace;
        }
    }

    /// <summary>
    /// General statistics around number of methods patched for specific assemblies as well as time taken for batched assembly call
    /// </summary>
    public class RedirectSetterMethodsFromCallingCodeResult
    {
        public List<RedirectSetterMethodsFromCallingCodeForAssyResult> AssemblyResults { get; }
        
        /// <summary>
        /// Time taken to process all assemblies
        /// </summary>
        public long TotalTimeTaken { get; }

        public RedirectSetterMethodsFromCallingCodeResult(List<RedirectSetterMethodsFromCallingCodeForAssyResult> assemblyResults, long totalTimeTaken)
        {
            AssemblyResults = assemblyResults;
            TotalTimeTaken = totalTimeTaken;
        }
    }

    /// <summary>
    /// General statistics around number of methods patched for specific assemblies as well as time taken for specific assembly
    /// </summary>
    public class RedirectSetterMethodsFromCallingCodeForAssyResult
    {
        /// <summary>
        /// File information about assembly
        /// </summary>
        public FileInfo AssemblyPath { get; }
        
        /// <summary>
        /// Finding methods to patch in assembly can take significant time. Once assembly has been processed, results can be cached for next time faster resolution. This property indicates if that's the case.
        /// </summary>
        public bool IsFindMethodsToPatchResolutionViaCache { get; }
        
        /// <summary>
        /// Time taken to find methods to patch. <see cref="IsFindMethodsToPatchResolutionViaCache"/> will indicate if results were retrieved from cache
        /// </summary>
        public long TimeTakenToFindMethodsToPatch { get; }
        
        /// <summary>
        /// Indicates whether found methods to patch were already patched, this is helpful where assembly needs to be made ready to be patched at some later point - quickly 
        /// </summary>
        public bool IsPatchAssemblyExecuted { get; internal set; }
        
        /// <summary>
        /// Time taken to redirect method calls to enable change tracking
        /// </summary>
        public long TimeTakenToPatchMethods { get; internal set; }
        
        /// <summary>
        /// Information about actual redirected calls
        /// </summary>
        public List<MethodInterceptionParams> MethodInterceptionParamEntries { get; }
        
        /// <summary>
        /// Time taken to patch specific methods
        /// </summary>
        public Dictionary<MethodInterceptionParams, long> TimeTakenPerMethodInterceptionParamToPatchMethods { get; internal set; }

        public RedirectSetterMethodsFromCallingCodeForAssyResult(FileInfo assemblyPath, long timeTakenToFindMethodsToPatch, List<MethodInterceptionParams> methodInterceptionParamEntries, bool isFindMethodsToPatchResolutionViaCache)
        {
            AssemblyPath = assemblyPath;
            TimeTakenToFindMethodsToPatch = timeTakenToFindMethodsToPatch;
            MethodInterceptionParamEntries = methodInterceptionParamEntries;
            IsFindMethodsToPatchResolutionViaCache = isFindMethodsToPatchResolutionViaCache;
        }
    }

    public class MethodInterceptionParams
    {
        /// <summary>
        /// Method that'll be patched / intercepted
        /// </summary>
        public MethodDefinition MethodDefinition { get; }
        
        /// <summary>
        /// Full method name that <see cref="MethodDefinition"/> is calling to and is the reason why the call will be patched
        /// </summary>
        public string PatchingDueToInterceptedMethodCallFullName { get; }

        public MethodInterceptionParams(MethodDefinition methodDefinition, string patchingDueToInterceptedMethodCallFullName)
        {
            MethodDefinition = methodDefinition;
            PatchingDueToInterceptedMethodCallFullName = patchingDueToInterceptedMethodCallFullName;
        }
    }
    
    [Serializable]
    public class TypeNameToFullNamePair
    {
        public string TypeName;
        public string FullMethodName;

        public TypeNameToFullNamePair(string typeName, string fullMethodName)
        {
            TypeName = typeName;
            FullMethodName = fullMethodName;
        }

        public TypeNameToFullNamePair()
        {
        }
    }

    [Serializable]
    public class CachedAssemblyWithMethodsToPatchInfo
    {
        private DateTime _cachedAssemblyFileCompilationTime;
        public DateTime CachedAssemblyFileCompilationTime
        {
            get
            {
                if (_cachedAssemblyFileCompilationTime == default)
                {
                    _cachedAssemblyFileCompilationTime = new DateTime(CachedAssemblyFileCompilationTimeTicks, DateTimeKind.Utc);
                }
                return _cachedAssemblyFileCompilationTime;
            }
            set
            {
                _cachedAssemblyFileCompilationTime = value;
                CachedAssemblyFileCompilationTimeTicks = _cachedAssemblyFileCompilationTime.Ticks; 
            }
        }

        public long CachedAssemblyFileCompilationTimeTicks;
        public string AssemblyFileFullPath;
        public List<TypeNameToFullNamePair> TypeNameToFullMethodNamesPairToPatch;


        public CachedAssemblyWithMethodsToPatchInfo(DateTime cachedAssemblyFileCompilationTime, string assemblyFileFullPath, List<TypeNameToFullNamePair> typeNameToFullMethodNamesPairToPatch)
        {
            CachedAssemblyFileCompilationTime = cachedAssemblyFileCompilationTime;
            AssemblyFileFullPath = assemblyFileFullPath;
            TypeNameToFullMethodNamesPairToPatch = typeNameToFullMethodNamesPairToPatch;
        }
    }

    [Serializable]
    public class MethodToPathCacheKey
    {
        public string AssemblyFullPath;
        public string TargetPatchMethodFullName;

        public MethodToPathCacheKey(string assemblyFullPath, string targetPatchMethodFullName)
        {
            TargetPatchMethodFullName = targetPatchMethodFullName;
            AssemblyFullPath = assemblyFullPath;
        }

        public MethodToPathCacheKey()
        {
        }

        public override bool Equals(object obj)
        {
            var methodToPathCacheKey = obj as MethodToPathCacheKey;
            if (methodToPathCacheKey == null) return false;
            
            return methodToPathCacheKey.AssemblyFullPath == this.AssemblyFullPath
                    && methodToPathCacheKey.TargetPatchMethodFullName == this.TargetPatchMethodFullName;
        }

        public override int GetHashCode()
        {
            return TargetPatchMethodFullName.GetHashCode() + AssemblyFullPath.GetHashCode();
        }
    }

    public class FindMethodsWithInstructionsToInterceptResult
    {
        public MethodToPathCacheKey MethodToPathCacheKey { get; }
        public bool IsFromCache { get; }
        public List<MethodInterceptionParams> MethodInterceptionParams { get; }

        public FindMethodsWithInstructionsToInterceptResult(MethodToPathCacheKey methodToPathCacheKey, bool isFromCache, List<MethodInterceptionParams> methodInterceptionParams)
        {
            MethodToPathCacheKey = methodToPathCacheKey;
            IsFromCache = isFromCache;
            MethodInterceptionParams = methodInterceptionParams;
        }
    }

    public enum ChangeType
    {
        Unknown,
        Position,
        Rotation,
        Scale
        //TODO: how to handle rotation and position change in single go?
    }

    public class InterceptCallSetupParam
    {
        public Func<ModuleDefinition, MethodDefinition> GetMethodToFind { get; }
        public HarmonyMethod Transpiler { get; }

        public InterceptCallSetupParam(Func<ModuleDefinition, MethodDefinition> getMethodToFind, HarmonyMethod transpiler)
        {
            GetMethodToFind = getMethodToFind;
            Transpiler = transpiler;
        }
    }


    /// <summary>
    /// Base class for intercepted callbacks
    /// </summary>
    public abstract class InterceptedCallbackBase
    {
        /// <summary>
        /// Specific change type - position/rotation/scale
        /// </summary>
        public ChangeType Type { get; }

        protected InterceptedCallbackBase(ChangeType type)
        {
            Type = type;
        }
    }

    /// <summary>
    /// Intercepted call callback for Vector3
    /// </summary>
    public class InterceptedVector3Callback: InterceptedCallbackBase
    {
        public static readonly InterceptedVector3Callback Empty = new InterceptedVector3Callback(ChangeType.Position, (ilWeavedValues, newValue) => { });

        public InterceptedVector3SetMethod Handler { get; }

        public InterceptedVector3Callback(ChangeType type, InterceptedVector3SetMethod handler) : base(type)
        {
            Handler = handler;
        }
    }

    /// <summary>
    /// Intercepted call callback for Quaternion
    /// </summary>
    public class InterceptedQuaternionCallback: InterceptedCallbackBase
    {
        public static readonly InterceptedQuaternionCallback Empty = new InterceptedQuaternionCallback(ChangeType.Rotation, (ilWeavedValues, newValue) => { });
        
        public InterceptedQuaternionSetMethod Handler { get; }

        public InterceptedQuaternionCallback(ChangeType type, InterceptedQuaternionSetMethod handler) : base(type)
        {
            Handler = handler;
        }
    }
    
    /// <summary>
    /// Delegate which receives raw intercepted call values for Vector3
    /// </summary>
    public delegate void InterceptedVector3SetMethod(IlWeavedValuesArray ilWeavedValues, Vector3 newValue);
    
    /// <summary>
    /// Delegate which receives raw intercepted call values for Quaternion
    /// </summary>
    public delegate void InterceptedQuaternionSetMethod(IlWeavedValuesArray ilWeavedValues, Quaternion newValue);
}