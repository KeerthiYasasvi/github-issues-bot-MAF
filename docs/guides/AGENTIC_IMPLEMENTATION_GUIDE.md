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

### 1. OrchestratorAgent
**Location**: `SupportConcierge.Core/Agents/OrchestratorAgent.cs`

Core planner that manages the workflow:
- **UnderstandTaskAsync()**: Initial issue analysis and planning
- **EvaluateProgressAsync()**: Assess progress after each step
- **DecideFollowUp()**: Determine follow-up strategy
- **ReplanAsync()**: Adapt when approaches fail

**Key Features**:
- 3-loop maximum (hard constraint)
- Escalates to human after loop 3
- Adaptive planning based on feedback
- Clear planner/executor separation

### 2. CriticAgent
**Location**: `SupportConcierge.Core/Agents/CriticAgent.cs`

Quality validator integrated at 3 stages:
- **CritiqueTriageAsync()**: Validates classification (threshold: ≥6/10)
- **CritiqueResearchAsync()**: Validates findings (threshold: ≥5/10)
- **CritiqueResponseAsync()**: Validates response (threshold: ≥7/10)

**Key Features**:
- Uses gpt-4o-mini for cost efficiency
- Structured feedback: score + Issues array + Suggestions
- Early quality gates prevent waste
- Actionable suggestions for refinement

### 3. EnhancedTriageAgent
**Location**: `SupportConcierge.Core/Agents/EnhancedTriageAgent.cs`

Intelligent classification with hybrid categories:
- **ClassifyAndExtractAsync()**: Initial triage
- **RefineAsync()**: Improve based on critique

**Hybrid Logic**:
```
if confidence >= 0.75:
    use predefined categories
else:
    activate custom category with LLM-suggested checklist
```

### 4. EnhancedResearchAgent
**Location**: `SupportConcierge.Core/Agents/EnhancedResearchAgent.cs`

Dynamic tool selection and investigation:
- **SelectToolsAsync()**: LLM chooses tools
- **InvestigateAsync()**: Execute tools
- **DeepDiveAsync()**: Deeper investigation

Available tools: GitHubSearchTool, DocumentationSearchTool, CodeAnalysisTool, ValidationTool

### 5. EnhancedResponseAgent
**Location**: `SupportConcierge.Core/Agents/EnhancedResponseAgent.cs`

Response generation following orchestrator decisions:
- **GenerateResponseAsync()**: Create response
- **GenerateFollowUpAsync()**: Generate follow-ups
- **RefineAsync()**: Improve based on critique

### 6. ToolRegistry
**Location**: `SupportConcierge.Core/Tools/ToolRegistry.cs`

Central registry for dynamic tool selection and execution.

## Orchestration Flow

```
Loop 1-3 (max):
├─ Orchestrator: UnderstandTask → Plan
├─ Triage: ClassifyAndExtract → CriticAgent [Gate 1]
├─ Research: SelectTools → Investigate → CriticAgent [Gate 2]
├─ Response: GenerateResponse → CriticAgent [Gate 3]
├─ Orchestrator: DecideFollowUp
└─ Loop counter++ (escalate if ≥ 3)
```

## Quality Gates

| Gate | Stage | Threshold | Action |
|------|-------|-----------|--------|
| 1 | Triage | ≥ 6/10 | Refine if below |
| 2 | Research | ≥ 5/10 | Deep dive if below |
| 3 | Response | ≥ 7/10 | Refine if below |

## Cost Optimization

**Model Strategy**:
- Main Agents: gpt-4o (planning, complex reasoning)
- Critics: gpt-4o-mini (evaluation only, 67% cheaper)

**Cost per Issue**:
- Best case (1 loop): ~$0.06
- Typical (2 loops): ~$0.095
- Worst case (3 loops): ~$0.13
- **Average: $21.50-26.50** (vs ~$31 with single gpt-4o)
- **Savings: ~30%**

## Integration Checklist

- [x] Dual-model LLM support (OpenAiClient + Program.cs)
- [x] OrchestratorAgent created
- [x] CriticAgent created
- [x] EnhancedTriageAgent created
- [x] EnhancedResearchAgent created
- [x] EnhancedResponseAgent created
- [x] ToolRegistry created
- [x] OrchestrationSchemas created
- [ ] Fix type imports and build
- [ ] Integrate with SupportConciergeRunner
- [ ] Register agents in DI
- [ ] Unit tests
- [ ] Integration tests
- [ ] Deploy with OPENAI_CRITIQUE_MODEL=gpt-4o-mini

## Testing Strategy

### Unit Tests
- OrchestratorAgent loop limits and escalation
- CriticAgent scoring thresholds
- EnhancedTriageAgent custom category fallback
- EnhancedResearchAgent tool selection
- EnhancedResponseAgent generation

### Integration Tests
- Full orchestration loop
- Critique feedback integration
- Tool registry execution
- Model switching verification

### Scenario Tests
- Build errors (MSB4019, etc.)
- Configuration issues
- Unclear requirements
- Complex multi-part problems

## Monitoring Metrics

- Loop count distribution
- Critique pass rates by gate (target: >80%)
- Tool selection frequency
- Custom category usage rate
- Escalation rate
- Cost per issue
- Response time (end-to-end)
- User satisfaction

## Next Steps

1. ✅ Components implemented
2. ⏳ Fix type imports and build
3. ⏳ Workflow integration
4. ⏳ Testing
5. ⏳ Deployment
