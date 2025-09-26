using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;
using System.Linq; 

// NOTE: All data structures (AgentAction, AgentWorkingMemory, etc.) are assumed to be 
// defined elsewhere (e.g., AgentMemoryStructures.cs).

public class AgentController : MonoBehaviour
{
    // --- DEPENDENCIES (References used for injection) ---
    private GeminiService geminiService;
    private NavMeshAgent navMeshAgent;

    // --- MEMORY STATE (Kept here so it is serializable by Unity) ---
    public BehaviourCache behaviourCache = new BehaviourCache();
    public LongTermMemory longTermMemory = new LongTermMemory();
    private AgentWorkingMemory workingMemory = new AgentWorkingMemory();

    // --- ORCHESTRATION & TIMING (Controller's new responsibility) ---
    private AgentDecisionService decisionService;

    [Header("Decision Timing")]
    [Tooltip("How often to check the local cache for fast, reflexive actions.")]
    public float reflexiveInterval = 0.5f; 
    [Tooltip("How often to call the Gemini LLM for slow, deliberate actions.")]
    public float deliberativeInterval = 2.0f; 

    private float lastReflexiveTime;
    private float lastDeliberativeTime;

    void Start()
    {
        // 1. Get references to the necessary components.
        // Using FindFirstObjectByType to comply with modern Unity standards (non-deprecated).
        geminiService = FindFirstObjectByType<GeminiService>();
        if (geminiService == null)
        {
            Debug.LogError("GeminiService not found in the scene! Please add it to a GameObject.");
            return;
        }

        navMeshAgent = GetComponent<NavMeshAgent>();
        if (navMeshAgent == null)
        {
            Debug.LogError("NavMeshAgent not found on this GameObject! Please add it.");
            return;
        }

        // 2. Initialize the Decision Service (Dependency Injection).
        // Pass all dependencies and memory state by reference.
        decisionService = new AgentDecisionService(
            geminiService,
            navMeshAgent,
            behaviourCache,
            workingMemory,
            longTermMemory
        );

        // 3. Set initial timers to allow immediate checks on Start.
        // We subtract the interval so the first check immediately passes the time gate.
        lastReflexiveTime = Time.time - reflexiveInterval;
        lastDeliberativeTime = Time.time - deliberativeInterval;
    }

    /// <summary>
    /// Update runs every frame, but the decision calls are gated by time checks,
    /// preventing unnecessary resource use on every cycle.
    /// </summary>
    void Update()
    {
        // --- FAST PATH CHECK (Reflexive - Cache) ---
        if (Time.time - lastReflexiveTime >= reflexiveInterval)
        {
            // Call the service, which is now responsible only for logic.
            decisionService.CheckReflexiveAction();
            
            // Reset the controller's timer for the next check.
            lastReflexiveTime = Time.time;
        }
        
        // --- SLOW PATH CHECK (Deliberative - LLM) ---
        if (Time.time - lastDeliberativeTime >= deliberativeInterval)
        {
            // Call the service asynchronously. The service itself handles the 
            // concurrency flag to prevent multiple API calls.
            decisionService.CheckDeliberativeActionAsync();
            
            // Reset the controller's timer immediately, even though the async call is still running.
            // This starts the new 2.0s countdown now.
            lastDeliberativeTime = Time.time;
        }
    }
}
