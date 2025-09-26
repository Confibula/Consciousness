using System;
using System.Collections.Generic;
using System.Linq;

// This file contains all the serializable data structures (models) used by the AgentController
// for managing memory, actions, and caching.

/// <summary>
/// 1. The primary executable command/action structure. 
/// This is returned by the LLM and stored in the Behaviour Cache.
/// </summary>
[Serializable]
public class AgentAction
{
    // The high-level decision (e.g., 'move', 'jump' (space-bar), 'wait', 'write').
    public string action;
    
    // Movement coordinates (for 'move' action).
    public float target_x;
    public float target_y;
    public float target_z;

    // Direction the agent should look (for 'look' or 'move' actions).
    public float camera_direction_x; 
    public float camera_direction_y;

    // If action is to write, the agent writes the text in the game-chat (or serves as the LLM's monologue).
    public string text;

    // The confidence level in the chosen action (0.0 to 1.0).
    public float confidence;
}

/// <summary>
/// 2. The Behaviour Cache Entry: Stores a successful context-action pair.
/// It stores the ContextPrompt string for fast, fuzzy-searchable lookup.
/// </summary>
[Serializable]
public class BehaviourCacheEntry
{
    // The generated prompt string that led to the successful action.
    public string ContextPrompt; // The prompt formed by AgentWorkingMemory
    public AgentAction Action;
}

/// <summary>
/// Wrapper class for the entire Behaviour Cache collection, making it serializable.
/// </summary>
[Serializable]
public class BehaviourCache
{
    public List<BehaviourCacheEntry> Entries = new List<BehaviourCacheEntry>();
}

/// <summary>
/// Helper structure to combine the executed action with the LLM's immediate reflection/outcome 
/// for easier storage in AgentWorkingMemory.
/// </summary>
[Serializable]
public class ActionOutcomeRecord
{
    public AgentAction Action;
    public string OutcomeSummary;

    public ActionOutcomeRecord(AgentAction action, string outcomeSummary) 
    {
        this.Action = action;
        this.OutcomeSummary = outcomeSummary;
    }
}

/// <summary>
/// **NEW:** Structured record for a single event in the 'MOMENT' array of Long-Term Memory.
/// This captures the context, action, and resulting outcome summary.
/// </summary>
[Serializable]
public class MomentRecord
{
    // Timestamp for when the event occurred.
    public string Timestamp;
    // The specific location the decision was made.
    public string SpatialContext; 
    // A snapshot of what the agent saw when the decision was made.
    public List<string> VisualObservationsSnapshot;
    // Captures the action outcome and summary (includes the action details).
    public string MomentActionOutcome; 
}

/// <summary>
/// **UPDATED:** Wrapper class for the entire Long-Term Memory collection, 
/// holding two separate, distinct arrays as requested.
/// </summary>
[Serializable]
public class LongTermMemory
{
    // Array 1: Stores contextual events (Spatial, Visual, Action Outcome)
    public List<MomentRecord> Moments = new List<MomentRecord>();
    
    // Array 2: Stores pure cognitive insights (context-free reflections)
    public List<string> Reflections = new List<string>();
}

/// <summary>
/// Represents the agent's current state, designed to mimic the components of 
/// human working memory for contextual input to the LLM.
/// </summary>
[Serializable]
public class AgentWorkingMemory
{
    // VISUO-SPATIAL SKETCHPAD (What is the world like right now?)
    
    // Agent's rounded position and orientation translated into a descriptive string.
    public string current_spatial_context = string.Empty; 

    // List of objects and key features currently visible or recently observed (fades over time).
    public List<string> recent_visual_observations = new List<string>();


    // VERBAL LOOP (What was recently read, heard or said?)
    
    // Previous exchanges with other agents.
    public List<string> recent_exchanges = new List<string>();


    // EPISODIC BUFFER
    
    // Stores a list of structured records, pairing the executed Action with the outcome summary/reflection.
    public List<ActionOutcomeRecord> recent_actions_and_outcomes = new List<ActionOutcomeRecord>();


    // CENTRAL EXECUTIVE (High-level Cognition)

    // Core traits, and self-defined purpose for future decisions.
    public List<string> core_self_concept = new List<string>();

    // Self-corrections and internal contradictions, e.g. "The sky is blue but for some reason my agent friend says the sky is red."
    // This list stores the cognitive insights that are *not* directly tied to a specific action outcome.
    public List<string> reflections = new List<string>();

    // Current, short-term plans, goals, and steps to execute.
    public List<string> plans = new List<string>();
}
