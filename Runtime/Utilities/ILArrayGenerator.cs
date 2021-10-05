using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace TransformChangesDebugger.API.Utilities
{
    internal class ILArrayGenerator
    {
        public static IEnumerable<CodeInstruction> PopulateArrayAtIndex(int index, params CodeInstruction[] loadInstructions)
        {
            yield return new CodeInstruction(OpCodes.Dup);
            yield return new CodeInstruction(OpCodes.Ldc_I4, index);

            foreach (var loadInstruction in loadInstructions) yield return loadInstruction;

            yield return new CodeInstruction(OpCodes.Stelem_Ref);
        }

        public static IEnumerable<CodeInstruction> CreateArray(int length, Type type)
        {
            yield return new CodeInstruction(OpCodes.Ldc_I4, length);
            yield return new CodeInstruction(OpCodes.Newarr, type);
        }
#if NET_4_6
        public static IEnumerable<CodeInstruction> CreateArrayWithValues(Type type, LocalBuilder storeAsVariable, List<List<CodeInstruction>> loadMultipleIndexesInstructions, int? forceArraySize = null)
        {
            var results = new List<CodeInstruction>();

            results.AddRange(CreateArray(forceArraySize.HasValue ? forceArraySize.Value : loadMultipleIndexesInstructions.Count, type));

            for (var i = 0; i < loadMultipleIndexesInstructions.Count; i++)
            {
                var loadIndexesInstructions = loadMultipleIndexesInstructions[i];
                results.AddRange(PopulateArrayAtIndex(i, loadIndexesInstructions.ToArray()));
            }
            
            results.Add( new CodeInstruction(OpCodes.Stloc, storeAsVariable));

            return results;
        }
#else
        public static IEnumerable<CodeInstruction> CreateArrayWithValues(Type type, object storeAsVariable, List<List<CodeInstruction>> loadMultipleIndexesInstructions, int? forceArraySize = null)
        {
	        throw new Exception("Tool only supports API Compatibility level 4.x");
        }  
#endif
    }
}