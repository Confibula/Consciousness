using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;
using System.Linq;

// NOTE: All memory and action data structures are defined in AgentMemoryStructures.cs

/// <summary>
/// This service handles all the core AI logic, including memory management, 
/// decision making (LLM calls or cache lookups), and action execution.
/// It implements a dual-rate architecture to separate fast, reflexive (cached) 
/// actions from slow, deliberative (LLM) actions.
/// The TIMING GATES have been moved to the AgentController to conserve resources.
/// </summary>
public class AgentDecisionService
{
    // --- DEPENDENCIES (Injected) ---
    private readonly GeminiService _geminiService;
    private readonly NavMeshAgent _navMeshAgent;
    private const float SimilarityThreshold = 0.8f;
    
    // --- STATE (Injected References to Controller's Memory) ---
    private readonly BehaviourCache _behaviourCache;
    private readonly AgentWorkingMemory _workingMemory;
    private readonly LongTermMemory _longTermMemory;

    // --- CONCURRENCY CONTROL ---
    // Flag to ensure only one deliberate call is in flight at a time.
    private bool _isDeliberating = false;

    /// <summary>
    /// Constructor: Injects all necessary dependencies and memory references.
    /// </summary>
    public AgentDecisionService(GeminiService geminiService, NavMeshAgent navMeshAgent,
                                BehaviourCache behaviourCache, AgentWorkingMemory workingMemory, 
                                LongTermMemory longTermMemory)
    {
        _geminiService = geminiService;
        _navMeshAgent = navMeshAgent;
        _behaviourCache = behaviourCache;
        _workingMemory = workingMemory;
        _longTermMemory = longTermMemory;

        // Initialize core identity elements for the LLM to use.
        _workingMemory.core_self_concept.Add("Goal: Reach the destination marker.");
        _workingMemory.core_self_concept.Add("Personality: Cautious and methodical.");
        _workingMemory.core_self_concept.Add("Purpose: Scout the environment for anomalies.");
        _workingMemory.plans.Add("Current primary plan is to walk towards the target, checking for dangers.");
    }

    /// <summary>
    /// FAST PATH (Reflexive Cycle): Checks only the local cache.
    /// This is synchronous and does not involve the LLM.
    /// </summary>
    public void CheckReflexiveAction()
    {
        // 1. Update memory snapshot for the current moment.
        UpdateWorkingMemory();

        // 2. Create the prompt from the structured memory.
        string prompt = GeneratePromptFromMemory(_workingMemory);
        
        // 3. Check the BEHAVIOUR CACHE (Reflexive brain) for a match.
        AgentAction cachedAction = FindBestCachedAction(prompt);

        if (cachedAction != null)
        {
            Debug.Log($"Using CACHED action with similarity score > {SimilarityThreshold}.");
            ExecuteAction(cachedAction, isCached: true); 
        }
    }

    /// <summary>
    /// SLOW PATH (Deliberative Cycle): Calls the Gemini LLM.
    /// The LLM's decision overrides any reflexive action currently taking place.
    /// </summary>
    public async void CheckDeliberativeActionAsync()
    {
        if (_isDeliberating)
        {
            return; // Exit if a deliberate call is already in flight.
        }

        // Set the flag immediately to prevent re-entry.
        _isDeliberating = true;
        
        // 1. Update memory snapshot for the current moment.
        UpdateWorkingMemory();

        // 2. Create the prompt from the structured memory.
        string prompt = GeneratePromptFromMemory(_workingMemory);
        
        // 3. Call the GeminiService (ASYNCHRONOUS).
        string jsonResponse = await _geminiService.GetRawResponse(prompt);

        _isDeliberating = false; // Release the lock once the response is back.
        
        if (!string.IsNullOrEmpty(jsonResponse))
        {
            // Parse the JSON into our C# object.
            try
            {
                AgentAction actionResponse = JsonUtility.FromJson<AgentAction>(jsonResponse);
                
                // 4. LLM decision overrides the current action (conflict resolution).
                // 5. Save the successful response to the BEHAVIOUR CACHE.
                BehaviourCacheEntry newEntry = new BehaviourCacheEntry
                {
                    ContextPrompt = prompt,
                    Action = actionResponse
                };
                _behaviourCache.Entries.Add(newEntry);
                
                // 6. Save the new Moment and Reflection to the split Long-Term Memory arrays.
                // Note: LTM is only updated when a new, non-cached decision is made.
                string momentOutcome = _workingMemory.recent_actions_and_outcomes.LastOrDefault()?.OutcomeSummary ?? actionResponse.action;

                MomentRecord moment = new MomentRecord
                {
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    SpatialContext = _workingMemory.current_spatial_context,
                    VisualObservationsSnapshot = new List<string>(_workingMemory.recent_visual_observations),
                    MomentActionOutcome = momentOutcome
                };

                _longTermMemory.Moments.Add(moment);

                string latestReflection = _workingMemory.reflections.LastOrDefault();
                if (!string.IsNullOrEmpty(latestReflection))
                {
                    _longTermMemory.Reflections.Add(latestReflection);
                }
                
                ExecuteAction(actionResponse, isCached: false);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse Gemini JSON response: {e.Message}. Response was: {jsonResponse}");
            }
        }
    }
    
    /// <summary>
    /// Updates the Working Memory with the latest data from the game environment.
    /// </summary>
    // TODO: When updating spatial_context, this will be done with barracude CNN. Needs to be implemented. And a further
    // comment on that. Currently no light-weight model has been used in the implementation elsewhere. These must be implemented!
    private void UpdateWorkingMemory()
    {
        // IMPORTANT: Rounding position makes the prompt text more consistent, 
        // improving fuzzy caching hit rates.
        Vector3 pos = _navMeshAgent.transform.position;
        // Includes orientation data (Rotation Y) in the spatial context string.
        float rotationY = _navMeshAgent.transform.rotation.eulerAngles.y;
        _workingMemory.current_spatial_context = $"Position X:{pos.x:F1}, Y:{pos.y:F1}, Z:{pos.z:F1}. Facing direction: {rotationY:F1} degrees.";

        // Placeholder for observation updates (e.g., from Raycasts or proximity triggers)
        if (_workingMemory.recent_visual_observations.Count == 0)
        {
            _workingMemory.recent_visual_observations.Add("A large oak tree is 10 meters ahead.");
            _workingMemory.recent_visual_observations.Add("The destination marker is visible to the East.");
        }
        
        // Ensure memory lists don't grow infinitely (maintaining working memory capacity limit)
        const int MAX_MEMORY_ITEMS = 5;
        if (_workingMemory.recent_actions_and_outcomes.Count > MAX_MEMORY_ITEMS)
        {
            _workingMemory.recent_actions_and_outcomes.RemoveAt(0);
        }
    }

    /// <summary>
    /// Formats the structured memory object into a single, comprehensive text prompt 
    /// for the Gemini model.
    /// </summary>
    private string GeneratePromptFromMemory(AgentWorkingMemory memory)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.AppendLine("You are an Autonomous Agent. Your output MUST be a JSON object with the fields: action, target_x, target_y, target_z, camera_direction_x, camera_direction_y, text, and confidence.");
        sb.AppendLine("Analyze the following structured working memory and decide the single best action (e.g., 'move', 'jump', 'wait', 'speak', 'look').");
        sb.AppendLine("If the action is 'move', set the target coordinates. If the action is 'look', set camera_direction_x/y.");
        sb.AppendLine("The 'text' field should contain your reasoning/internal monologue. If the action is 'speak', this text will be spoken.");
        sb.AppendLine("--- WORKING MEMORY SNAPSHOT ---");
        
        sb.AppendLine("1. CORE SELF-CONCEPT (Identity & Goals):");
        sb.Append(string.Join("\n - ", memory.core_self_concept));
        sb.AppendLine();
        
        sb.AppendLine("2. SPATIAL CONTEXT (Location & Visuals):");
        sb.AppendLine($"- LOCATION: {memory.current_spatial_context}");
        sb.AppendLine("- VISIBLE OBJECTS: " + string.Join(", ", memory.recent_visual_observations));
        sb.AppendLine();
        
        sb.AppendLine("3. VERBAL LOOP (Dialogue History):");
        sb.AppendLine("- LAST EXCHANGES: " + string.Join("; ", memory.recent_exchanges));
        sb.AppendLine();
        
        sb.AppendLine("4. EPISODIC HISTORY (Actions & Outcomes):");
        sb.AppendLine("- LAST ACTIONS: " + string.Join("; ", memory.recent_actions_and_outcomes.Select(r => $"[{r.Action.action}]: {r.OutcomeSummary}")));
        sb.AppendLine();
        
        sb.AppendLine("5. CENTRAL EXECUTIVE (Plans & Reflections):");
        sb.AppendLine("- CURRENT PLANS: " + string.Join("; ", memory.plans));
        sb.AppendLine("- RECENT REFLECTIONS: " + string.Join("; ", memory.reflections));
        
        sb.AppendLine("--- END MEMORY ---");
        sb.AppendLine("What is the next action (JSON ONLY)?");

        return sb.ToString();
    }

    /// <summary>
    /// Searches the Behaviour Cache for the most similar situation based on keyword overlap.
    /// </summary>
    private AgentAction FindBestCachedAction(string currentPrompt)
    {
        string[] currentKeywords = currentPrompt.ToLower().Split(new char[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        BehaviourCacheEntry bestMatch = null; 
        float highestScore = 0f;

        foreach (var entry in _behaviourCache.Entries)
        {
            string[] cachedKeywords = entry.ContextPrompt.ToLower().Split(new char[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            int commonKeywords = currentKeywords.Intersect(cachedKeywords).Count();

            float score = (float)commonKeywords / ((currentKeywords.Length + cachedKeywords.Length) / 2f);

            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = entry;
            }
        }

        if (highestScore >= SimilarityThreshold && bestMatch != null)
        {
            return bestMatch.Action;
        }

        return null;
    }

    
    /// <summary>
    /// Executes the final chosen action using the NavMeshAgent and logging the outcome.
    /// </summary>
    private void ExecuteAction(AgentAction action, bool isCached)
    {
        if (action == null)
        {
            return;
        }

        // Log the decision to working memory *before* executing.
        string outcomeSummary = $"Action: {action.action} to ({action.target_x:F1}, {action.target_z:F1}) | Monologue: {action.text}";
        
        // Adding a structured ActionOutcomeRecord to working memory.
        _workingMemory.recent_actions_and_outcomes.Add(new ActionOutcomeRecord(action, outcomeSummary));

        Debug.Log($"Agent Monologue: {action.text} (Source: {(isCached ? "Cache" : "Gemini")})");
        
        switch (action.action)
        {
            case "move":
                Vector3 targetPosition = new Vector3(action.target_x, action.target_y, action.target_z);
                _navMeshAgent.SetDestination(targetPosition);
                Debug.Log($"Agent moving to {targetPosition}");
                break;
            case "look":
                // TODO: Implement camera rotation logic using action.camera_direction_x/y
                Debug.Log($"Agent looking toward pitch/yaw: {action.camera_direction_x}, {action.camera_direction_y}");
                break;
            case "jump":
                // TODO: Implement jump logic here.
                Debug.Log("Agent attempting to jump.");
                break;
            case "speak":
                // This would trigger a chat message or TTS output.
                Debug.Log($"Agent spoke: {action.text}");
                break;
            default:
                Debug.LogWarning($"Unknown action: {action.action}");
                break;
        }
    }
}
