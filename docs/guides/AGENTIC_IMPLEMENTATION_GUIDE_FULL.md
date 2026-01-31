# Agentic System Implementation Guide

## Overview
This document outlines the implementation of a TRUE agentic system for the GitHub Issues Bot, replacing the deterministic DAG pipeline with autonomous agents that plan, select tools, and make decisions.

## Environment Variables (Dual-Model Setup)

The system now supports two models via environment variables:

```bash
# Primary model for agents (planning, triage, research, response)
OPENAI_MODEL=gpt-4o

# Optional: Critique model (evaluation only, defaults to primary if not set)
OPENAI_CRITIQUE_MODEL=gpt-4o-mini
```

### Backward Compatibility
- If `OPENAI_CRITIQUE_MODEL` is not set, the system uses `OPENAI_MODEL` for both agents and critics
- Existing deployments continue to work without changes
- Set `OPENAI_CRITIQUE_MODEL=gpt-4o-mini` to enable cost optimization (30% savings overall)

## Architecture Components

### 1. OrchestratorAgent (NEW)
**Location**: `SupportConcierge.Core/Agents/OrchestratorAgent.cs`

Core planner that manages the workflow:
- **UnderstandTaskAsync()**: Initial issue analysis and plan creation
- **EvaluateProgressAsync()**: Assess progress after each loop
- **DecideFollowUp()**: Determine if follow-ups are needed
- **ReplanAsync()**: Adapt strategy when approaches fail

**Key Features**:
- 3-loop maximum (hard constraint)
- Escalates to human after loop 3
- Creates adaptive plans based on feedback
- Clear separation of concerns: decides what to do, doesn't do it

**Usage**:
```csharp
var orchestrator = new OrchestratorAgent(llmClient, schemaValidator);
var plan = await orchestrator.UnderstandTaskAsync(context);
var decision = await orchestrator.EvaluateProgressAsync(context, plan, currentLoop, previousDecisions);
```

### 2. CriticAgent (NEW)
**Location**: `SupportConcierge.Core/Agents/CriticAgent.cs`

Quality validator integrated at 3 stages:

- **CritiqueTriageAsync()**: Validates classification & extraction (threshold: score >= 6)
- **CritiqueResearchAsync()**: Validates investigation findings (threshold: score >= 5)
- **CritiqueResponseAsync()**: Validates response quality (threshold: score >= 7)

**Key Features**:
- Uses gpt-4o-mini for cost efficiency (evaluation ≠ generation)
- Returns structured feedback with specific issues + suggestions
- Early quality gates prevent downstream waste
- CritiqueResult: score (1-10) + Issues array + Suggestions

**CritiqueResult Structure**:
```json
{
  "score": 7,
  "reasoning": "Good classification with minor gaps",
  "issues": [
    {
      "category": "missing_info",
      "problem": "No version info extracted",
      "suggestion": "Ask for Python version in follow-up",
      "severity": 2
    }
  ],
  "suggestions": ["Add version extraction", "Clarify error context"],
  "is_passable": true
}
```

**Usage**:
```csharp
var critic = new CriticAgent(criticLlmClient, schemaValidator);
var critique = await critic.CritiqueTriageAsync(context, triageResult);
if (!critique.IsPassable)
{
    // Refine with feedback
    var refined = await triageAgent.RefineAsync(context, triageResult, critique);
}
```

### 3. EnhancedTriageAgent
**Location**: `SupportConcierge.Core/Agents/EnhancedTriageAgent.cs`

Intelligent classification with fallback mechanism:

- **ClassifyAndExtractAsync()**: Initial triage
- **RefineAsync()**: Improve based on critique feedback

**Hybrid Category Logic**:
```
if confidence >= 0.75:
    use predefined categories
else:
    activate custom category with LLM-suggested checklist
```

**Predefined Categories**:
- build_issue, runtime_error, dependency_conflict, documentation_issue
- feature_request, bug_report, configuration_error, environment_setup

**Custom Category Object**:
```json
{
  "name": "Third-Party Integration Issue",
  "description": "Problem with third-party service integration",
  "required_fields": ["Integration name", "Error code", "Configuration details"]
}
```

**Usage**:
```csharp
var triage = new EnhancedTriageAgent(llmClient, schemaValidator);
var result = await triage.ClassifyAndExtractAsync(context);
if (result.UsesCustomCategory)
{
    // Handle custom category pathway
}
```

### 4. EnhancedResearchAgent
**Location**: `SupportConcierge.Core/Agents/EnhancedResearchAgent.cs`

Dynamic tool selection and investigation:

- **SelectToolsAsync()**: LLM chooses tools based on issue
- **InvestigateAsync()**: Execute selected tools and synthesize findings
- **DeepDiveAsync()**: Deeper investigation when critique indicates gaps

**Available Tools via ToolRegistry**:
- GitHubSearchTool: Search related issues/discussions
- DocumentationSearchTool: Search docs and wiki
- CodeAnalysisTool: Analyze versions, dependencies, configuration
- ValidationTool: Validate setup and environment

**Tool Selection Result**:
```json
{
  "selected_tools": [
    {
      "tool_name": "GitHubSearchTool",
      "reasoning": "Find similar build errors",
      "query_parameters": {"query": "MSB4019", "search_type": "issues"}
    }
  ],
  "investigation_strategy": "Search for similar build errors, then analyze root cause",
  "expected_findings": ["Similar error reports", "Solution patterns"]
}
```

**Usage**:
```csharp
var research = new EnhancedResearchAgent(llmClient, schemaValidator);
var toolSelection = await research.SelectToolsAsync(context, triageResult);
// Execute tools...
var investigation = await research.InvestigateAsync(context, triageResult, toolSelection, toolResults);
```

### 5. EnhancedResponseAgent
**Location**: `SupportConcierge.Core/Agents/EnhancedResponseAgent.cs`

Generates responses and follow-ups following orchestrator decisions:

- **GenerateResponseAsync()**: Create brief/response from findings
- **GenerateFollowUpAsync()**: Generate follow-up questions (if orchestrator requests)
- **RefineAsync()**: Improve based on critique feedback

**Responsibilities**:
- Doesn't decide if follow-ups are needed (Orchestrator does)
- Generates what Orchestrator tells it to generate
- Clear executor role

**Response Structure**:
```json
{
  "brief": {
    "title": "Solution for Build Error MSB4019",
    "summary": "This error occurs when...",
    "solution": "1. Update... 2. Configure...",
    "explanation": "The root cause is...",
    "next_steps": ["Verify solution", "Update config"]
  },
  "follow_ups": ["What version are you using?"],
  "requires_user_action": true
}
```

**Usage**:
```csharp
var response = new EnhancedResponseAgent(llmClient, schemaValidator);
var brief = await response.GenerateResponseAsync(context, triageResult, investigation);
if (orchestrator_decided_follow_ups_needed)
{
    var followUps = await response.GenerateFollowUpAsync(context, triageResult, investigation, brief);
}
```

### 6. ToolRegistry
**Location**: `SupportConcierge.Core/Tools/ToolRegistry.cs`

Central registry for dynamic tool selection:

```csharp
var toolRegistry = new ToolRegistry();

// Execute a tool
var result = await toolRegistry.ExecuteAsync("GitHubSearchTool", 
    new Dictionary<string, string> { {"query", "MSB4019"} });

// Get all tools for LLM
var descriptions = toolRegistry.GetToolDescriptionsForLlm();

// Get tools by type
var searchTools = toolRegistry.GetByType("search");
```

## Orchestration Flow

### Main Loop (Max 3 Iterations)

```
Loop 1:
├─ Orchestrator: UnderstandTask() → Plan
├─ Triage: ClassifyAndExtract()
├─ Critic: CritiqueTriageAsync() [Gate 1]
├─ Research: SelectTools() & Investigate()
└─ Critic: CritiqueResearchAsync() [Gate 2]

Loop 2 (if needed):
├─ Orchestrator: EvaluateProgress() → Decision
├─ If "research": Research: DeepDiveAsync()
├─ Critic: CritiqueResearchAsync() [Gate 2 again]
└─ ...

Loop 3 (final):
├─ Response: GenerateResponseAsync()
├─ Critic: CritiqueResponseAsync() [Gate 3]
├─ Orchestrator: DecideFollowUp()
└─ If yes: Response: GenerateFollowUpAsync()

After Loop 3:
└─ If not resolved → Escalate to human
```

## Quality Gates (In-Loop Critique)

### Gate 1: Triage Validation
- **Agent**: CriticAgent.CritiqueTriageAsync()
- **Threshold**: Score >= 6/10
- **Fail Action**: TriageAgent.RefineAsync() with feedback
- **Issues**: Hallucination, low confidence, missing extraction

### Gate 2: Research Validation
- **Agent**: CriticAgent.CritiqueResearchAsync()
- **Threshold**: Score >= 5/10
- **Fail Action**: ResearchAgent.DeepDiveAsync() with gap analysis
- **Issues**: Incomplete findings, speculation, missing investigation

### Gate 3: Response Validation
- **Agent**: CriticAgent.CritiqueResponseAsync()
- **Threshold**: Score >= 7/10
- **Fail Action**: ResponseAgent.RefineAsync() with feedback
- **Issues**: Clarity, accuracy, completeness, tone

## Cost Optimization (30% Reduction)

### Model Strategy
```
Main Agents (Complex Reasoning):
  - OrchestratorAgent → gpt-4o
  - EnhancedTriageAgent → gpt-4o
  - EnhancedResearchAgent → gpt-4o
  - EnhancedResponseAgent → gpt-4o

Critics (Simple Evaluation):
  - CriticAgent → gpt-4o-mini (67% cheaper)
```

### Cost Analysis (Per Issue)
```
Best Case (1 loop, all gates pass):
  - 3 main calls (gpt-4o): $0.015 × 3 = $0.045
  - 3 critique calls (gpt-4o-mini): $0.005 × 3 = $0.015
  - Total: ~$0.06

Typical Case (2 loops, 1 retry):
  - 5-6 main calls (gpt-4o): $0.075
  - 4-5 critique calls (gpt-4o-mini): $0.020
  - Total: ~$0.095

Worst Case (3 loops, all gates fail once):
  - 7-8 main calls (gpt-4o): $0.105
  - 5-6 critique calls (gpt-4o-mini): $0.025
  - Total: ~$0.13

Average: $21.50-26.50 per issue (vs ~$31 with single gpt-4o model)
Savings: ~30%
```

## Integration Steps

### 1. Update Program.cs (Already Done)
```csharp
// Create dual-model clients
ILlmClient agentLlmClient = CreateLlmClient(metrics, modelOverride: null);
ILlmClient criticLlmClient = CreateLlmClient(metrics, modelOverride: Environment.GetEnvironmentVariable("OPENAI_CRITIQUE_MODEL"));

// Use in orchestration workflow
var orchestrator = new OrchestratorAgent(agentLlmClient, schemaValidator);
var critic = new CriticAgent(criticLlmClient, schemaValidator);
```

### 2. Register Agents in DI Container (TODO)
```csharp
services.AddSingleton(orchestrator);
services.AddSingleton(triageAgent);
services.AddSingleton(researchAgent);
services.AddSingleton(responseAgent);
services.AddSingleton(criticAgent);
services.AddSingleton(toolRegistry);
```

### 3. Replace Workflow Execution (TODO)
Instead of fixed DAG, use orchestrator-driven loop:
```csharp
for (int loop = 0; loop < 3; loop++)
{
    var decision = await orchestrator.EvaluateProgressAsync(...);
    switch (decision.Action)
    {
        case "research":
            // Research phase
            break;
        case "respond":
            // Response phase
            break;
        case "escalate":
            // Escalate to human
            break;
    }
}
```

## JSON Schemas

All agent outputs use strict JSON schemas defined in `OrchestrationSchemas`:
- **OrchestratorPlan**: Plan structure
- **CritiqueResult**: Critique feedback
- **TriageRefinement**: Triage output
- **ResearchResult**: Investigation findings
- **ResponseGeneration**: Brief/response
- **ToolSelection**: Tool selection

## Error Handling

### LLM Call Failures
- Critic returns default low score (3/10) with error issue
- Agent uses previous result or default safe fallback
- Logged for monitoring

### Invalid Responses
- JSON schema validation catches malformed responses
- Fallback to retry or safe default
- Circuit breaker after N failures

### Tool Execution Failures
- Tool execution errors caught and returned in ToolResult
- Research continues with available findings
- No cascading failures

## Testing Recommendations

### Unit Tests
- **OrchestratorAgent**: Loop limit enforcement, escalation logic
- **CriticAgent**: Threshold evaluation, feedback generation
- **EnhancedTriageAgent**: Custom category fallback, refinement
- **EnhancedResearchAgent**: Tool selection, investigation synthesis
- **EnhancedResponseAgent**: Response generation, follow-up generation

### Integration Tests
- **Full Loop**: Issue → Orchestrator → Agents → Response
- **Critique Loop**: Failure case → Critique → Refinement → Pass
- **Tool Selection**: Orchestrator → Research → Tool Registry → Execution
- **Model Switching**: Verify gpt-4o-mini used only for critique

### Scenario Tests
- Build errors (MSB4019 etc)
- Configuration issues
- Unclear requirements
- Complex multi-part problems

## Monitoring & Metrics

Key metrics to track:
- Loop count distribution (should be mostly 1-2 loops)
- Critique pass rates by gate (target: >80%)
- Tool selection frequency
- Custom category usage
- Escalation rate
- Cost per issue
- Response time

## Next Steps

1. ✅ Dual-model support implemented
2. ✅ Core agents created (Orchestrator, Critic, Triage, Research, Response)
3. ✅ ToolRegistry implemented
4. ⏳ Integrate with existing workflow (replace SupportConciergeRunner)
5. ⏳ Register agents in DI container
6. ⏳ Create orchestration loop in runner
7. ⏳ Add unit tests for each agent
8. ⏳ Add integration tests for full loop
9. ⏳ Monitor metrics and optimize
10. ⏳ Deploy with gpt-4o-mini enabled

## Deployment Checklist

- [ ] Set OPENAI_MODEL=gpt-4o
- [ ] Set OPENAI_CRITIQUE_MODEL=gpt-4o-mini
- [ ] Deploy updated code
- [ ] Monitor cost reduction (target: 30% savings)
- [ ] Monitor loop distribution (target: avg < 1.5 loops)
- [ ] Monitor quality metrics (target: >85% first-pass)
- [ ] Collect feedback on response quality
- [ ] Adjust thresholds if needed
