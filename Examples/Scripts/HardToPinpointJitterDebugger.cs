using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TransformChangesDebugger.API;
using TransformChangesDebugger.API.Patches;
using UnityEngine;

public class HardToPinpointJitterDebugger : MonoBehaviour
{
    [SerializeField] private TrackTransformChanges TrackingObject;
    
    [Header("Before and after approach")]
    [SerializeField] private float WaitForNSecondsBeforePrintingInformation = 1f;
    [SerializeField] private float DistanceThresholdBetweenNewAndOldValue = 3f;
    [SerializeField] private int FrameCountToReviewOnCheck = 50;
    
    [Header("Review Modifiers approach")]
    [SerializeField] private float PrintModifiersEveryNSeconds = 5f;

    [Header("Disable changes done by modifiers")] [SerializeField]
    private bool DisableChangesDoneByModifiers = false;
    [SerializeField] private string DisableChangesDoneByModifiersContaining = "FrustratingDisruptor";

    
    void Awake()
    {
        //Tracking needs to be initialized, best done before other scripts had a chance to run
        InitializeTracking();
        
        //1ST SCENARIO:
        //we know that transform jitters in some frame, every n seconds we're going to review last FrameCountToReviewOnCheck to see if any change causes vectors to get further than DistanceThresholdBetweenNewAndOldValue
        //reason behind being valid changes should create circular motion, jitter is likely causing big jump 
        StartCoroutine(TryToCaptureSuspiciousChangeBasedOnBeforeAndAfterChangeDistanceDifference());
        
        //2ND SCENARIO:
        //in this scenario we'd like to understand which object is changing ours, this will be then used to find code / objects and review what it is doing
        //reason being you're quite familiar with what should happen to the object and quite likely will be able to quickly spot something odd making changes - just need to make sure that's visible to you
        StartCoroutine(TryToCaptureSuspiciousChangeBasedOnModifiersMakingChanges());
        
        //3RD SCENARIO
        //by that stage you already know 'FrustratingDisruptor' is making some odd changes that are likely causing the issue, just to confirm
        //you'll turn that change off at runtime to see if issue is gone (just tick DisableChangesDoneByModifiers in editor)
        SkipSpecificChangesIfEnabled();
    }
    
    static void InitializeTracking()
    {
        var userCodeAssembly = new FileInfo(Assembly.GetExecutingAssembly().Location);
        var thirdPartyToolAssembly = new FileInfo(userCodeAssembly.Directory.FullName + "/ThirdPartyTool.dll");
        
        var userChosenAssembliesToPatch = new List<FileInfo>() { userCodeAssembly }; //you can specify which assemblies are patched (to save time)
        var allAvailableAssembliesToPatch = new List<FileInfo> { userCodeAssembly, thirdPartyToolAssembly }; //and you also specify all available assemblies, this is mainly for performance-statistics that GUI can use
        
        TransformChangesDebuggerManager.IsTrackingEnabled = true;
        TransformChangesDebuggerManager.Initialize(allAvailableAssembliesToPatch, userChosenAssembliesToPatch);
    }

    IEnumerator TryToCaptureSuspiciousChangeBasedOnBeforeAndAfterChangeDistanceDifference()
    {
        while (true)
        {
            yield return new WaitForSeconds(WaitForNSecondsBeforePrintingInformation);
            
            for (var frameIndexAdjustment = 0; frameIndexAdjustment < FrameCountToReviewOnCheck; frameIndexAdjustment++) // we're going to check changes for last FrameCountToReviewOnCheck frames that had changes captured
            {
                //let's get changes that were captured for object
                var newestChanges = TransformChangesTracker.GetFrameChangesForTrackedObject(
                    TransformChangesTracker.GetNewestFrameNumberWithTrackedChanges(frameIndexAdjustment), //resolves latest frame index with changes
                    TrackingObject
                );
                
                //check change before and after vales for distance over threshold
                var changeWhichCreatesDistanceOverThreshold = newestChanges.FirstOrDefault(c => Vector3.Distance((Vector3) c.NewValue, (Vector3) c.ValueBeforeChange) > DistanceThresholdBetweenNewAndOldValue);
                if (changeWhichCreatesDistanceOverThreshold != null)
                {
                    //you can put a debugger breakpoint here to inspect further, you should quickly find FrustratingDisruptor is causing issues
                    Debug.Log($"Following change has moved transform suspiciously (CallingObjectName: {changeWhichCreatesDistanceOverThreshold.CallingObject.name}, " +
                              $"NewValue: {changeWhichCreatesDistanceOverThreshold.NewValue}, " +
                              $"ValueBeforeChange: {changeWhichCreatesDistanceOverThreshold.ValueBeforeChange})", 
                        changeWhichCreatesDistanceOverThreshold.CallingObject
                    );
                }
            }
        }
    }

    IEnumerator TryToCaptureSuspiciousChangeBasedOnModifiersMakingChanges()
    {
        while (true)
        {
            yield return new WaitForSeconds(PrintModifiersEveryNSeconds);
            
            // get modifiers that changed tracked object (in any captured frame)
            var modifiers = TransformChangesTracker.GetModifiers(TrackingObject);
            Debug.Log($"Following modifiers changed: {TrackingObject.name}");
            for (var i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                Debug.Log($"Modifier ({i}) {modifier.CallingObjectFullPath}: via method {modifier.CallingFromMethodName}", modifier.CallingObject);
            }
        }
    }

    void SkipSpecificChangesIfEnabled()
    {
        //set up predicate that'll look for changes originating from object containing 'DisableChangesDoneByModifiersContaining'
        TransformChangesDebuggerManager.SkipTransformChangesFor((IlWeavedValuesArray ilWeavedValuesArray, Component changingComponent) =>
        {
            if (!DisableChangesDoneByModifiers) return false;
                
            if (changingComponent.transform == TrackingObject.transform)
            {
                if (ilWeavedValuesArray.CallingObject.name.Contains(DisableChangesDoneByModifiersContaining))
                {
                    return true; //skip change execution
                    //actual change entry will still be created with WasChangeSkipped = true
                }
            }
            
            return false;
        });
        
        //you can also set up SkipTransformChangesFor and pass specific TransformModifier
    }
}
