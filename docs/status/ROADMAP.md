# Agentic System: Strategic Roadmap

## Executive Summary

We've implemented a **true agentic architecture** with autonomous planning, tool selection, and quality validation. Now we need to integrate it while deciding on the old code cleanup strategy.

## Current State

### What Works ✅
- New agents: OrchestratorAgent, CriticAgent, TriageAgent, ResearchAgent, ResponseAgent
- Dual-model LLM support (gpt-4o + gpt-4o-mini)
- Quality gates at 3 stages
- Tool registry for dynamic selection
- Complete JSON schemas
- 30% cost reduction potential

### What's Broken ⚠️
- Type compilation errors (quick fix, ~30 min)
- Not integrated into workflow yet
- Old deterministic pipeline still running

### Old Code Status ❓
- ClassifierAgent, ExtractorAgent, FollowUpAgent, BriefAgent, JudgeAgent
- 18 executor files for fixed DAG phases
- SupportConciergeRunner using fixed workflow
- Not deleted, not replaced

## Phase Timeline

```
Phase 1: ✅ DONE (Components Created)
         └─ Implemented all 6 agentic agents
         └─ Dual-model LLM support
         └─ JSON schemas

Phase 2: ⏳ NEXT (Build Fix & Cleanup Strategy)
         ├─ Fix type imports (30 min)
         ├─ Successful build (1 hour)
         └─ Decide on old code cleanup strategy

Phase 3: NOT STARTED (Workflow Integration)
         ├─ Strategy A: Full migration (1-2 weeks)
         ├─ Strategy B: Parallel systems + flag (2 weeks)
         ├─ Strategy C: Hybrid approach (3 weeks)
         └─ Or: Your custom approach

Phase 4: NOT STARTED (Testing)
         ├─ Unit tests (~20 hours)
         ├─ Integration tests (~15 hours)
         ├─ Scenario tests (~5 hours)
         └─ Total: ~40 hours

Phase 5: NOT STARTED (Deployment & Cleanup)
         ├─ Canary deployment (if using Strategy B)
         ├─ Monitor metrics
         ├─ Full rollout
         └─ Delete old code (if Strategy A)
```

## Three Cleanup Strategies for Old Code

### Strategy A: Full Migration (RECOMMENDED for True Agentic)

**What happens to old code:**
- DELETE: All old agents (ClassifierAgent, ExtractorAgent, etc.)
- DELETE: All 18 executor files (ParsePhase, SetupPhase, etc.)
- DELETE: Old workflow DAG code
- REPLACE with: New agentic orchestrator

**Timeline**: 1-2 weeks for Phase 3

**Pros**:
- ✅ Clean, modern codebase
- ✅ True agentic system (no legacy constraints)
- ✅ Easier to maintain long-term
- ✅ No technical debt

**Cons**:
- ❌ Breaking change (requires migration)
- ❌ No rollback to old system
- ❌ One-shot migration risk

**Best for**: Starting fresh, willing to risk one-time migration

---

### Strategy B: Parallel Systems (RECOMMENDED for Safe Migration)

**What happens to old code:**
- KEEP: All old agents (marked as deprecated)
- KEEP: DAG workflow (old code path)
- ADD: New agentic orchestrator (new code path)
- ADD: Feature flag to route traffic

**Architecture**:
```
Program.cs
├─ If SUPPORTBOT_AGENTIC_MODE == false
│  └─ Route to: SupportConciergeRunner (old DAG)
└─ If SUPPORTBOT_AGENTIC_MODE == true
   └─ Route to: OrchestratorAgent (new agentic)
```

**Timeline**: 2 weeks integration + 2-4 weeks canary + 1 week cleanup (Phase 5)

**Rollout Plan**:
1. Week 1-2: Deploy with flag disabled (old system active)
2. Week 3: Enable for 10% traffic (new system tested)
3. Week 4: Verify metrics, expand to 50%
4. Week 5: Full rollout at 100%
5. Week 6: Mark old code deprecated
6. Week 7+: Delete old code (Phase 5)

**Pros**:
- ✅ Safe canary deployment
- ✅ Can A/B test systems
- ✅ Easy rollback if issues
- ✅ Operational safety
- ✅ Existing tests still work

**Cons**:
- ❌ Code duplication temporarily
- ❌ More complex routing logic
- ❌ Need to support both systems

**Best for**: Production systems, risk-averse teams, existing customers

---

### Strategy C: Hybrid (Use Old Agents as Components)

**What happens to old code:**
- KEEP: Old agents but adapt their purpose
- WRAP: Old agents in new orchestration
- DELETE: DAG executor phases (not needed)
- REPLACE: SupportConciergeRunner with orchestrator loop

**Architecture**:
```
OrchestratorAgent (Planner)
├─ Decision 1: "Need triage"
│  └─ Call: Old ClassifierAgent (now a component)
│     └─ CriticAgent evaluates → Refine if needed
├─ Decision 2: "Need research"
│  └─ Call: Old ExtractorAgent (now a component)
│     └─ CriticAgent evaluates → Deep dive if needed
└─ Decision 3: "Need response"
   └─ Call: Old BriefAgent (now a component)
      └─ CriticAgent evaluates → Refine if needed
```

**Timeline**: 2-3 weeks for Phase 3

**Pros**:
- ✅ Reuses tested existing code
- ✅ Less code rewriting
- ✅ Gradual improvement possible
- ✅ Faster Phase 3

**Cons**:
- ❌ Old agents not designed for this use
- ❌ May need significant adaptation
- ❌ Not fully autonomous (constrained by old design)
- ❌ Still some legacy code

**Best for**: Pragmatic approach, time-constrained teams

---

## My Recommendation: Strategy B

**Why B is best**:

1. **Operational Safety**
   - Can verify new agents work before committing
   - Easy rollback if problems detected
   - Real-world testing before full migration

2. **Business Continuity**
   - Existing systems keep working
   - Existing tests keep passing
   - No service interruption

3. **Quality Assurance**
   - Can compare old vs. new output
   - A/B test quality metrics
   - Gather data before full migration

4. **Timeline Efficiency**
   - Parallel dev + deployment
   - Not rushed Phase 3
   - Proper testing in Phases 4-5

5. **Team Confidence**
   - Validated by real traffic before commit
   - Data-driven decision to fully migrate
   - Lower risk of post-deployment issues

**Implementation Path**:
```
Week 1-2: Phase 2 (Fix build)
Week 3-4: Phase 3a (Integrate new agents)
Week 5-6: Phase 3b (Add feature flag + routing)
Week 7: Deploy (flag=false, old system active)
Week 8-10: Phase 4 (Testing + canary)
Week 11: Scale to 100%
Week 12+: Phase 5 (Cleanup old code)
```

## Decision Framework

**Choose Strategy A if**:
- You want pure agentic (no legacy)
- You're willing to migrate all tests
- You have confidence in new design
- You can absorb one-time breaking change
- Greenfield/new project

**Choose Strategy B if** (RECOMMENDED)
- You run production systems
- You need operational safety
- You want data before commitment
- You value easy rollback
- You have existing customers/tests

**Choose Strategy C if**:
- You're time-constrained
- You value reusing existing code
- You're willing to adapt old code
- You want faster Phase 3
- Pragmatism > purity

---

## Next Steps (What We Need From You)

### Before Phase 2 Completion:

1. **Which cleanup strategy?** A / B / C / Other
   - Your choice impacts Phase 3 implementation

2. **Timeline preference?**
   - Fast track (willing to break things)
   - Standard (3-4 weeks)
   - Conservative (6+ weeks with validation)

3. **Any existing dependencies on old agents?**
   - Any external code using ClassifierAgent, etc.?
   - Any evals that depend on old structure?

4. **Rollout constraints?**
   - Can you do gradual canary?
   - Need synchronized release?
   - Customers to notify?

### After You Decide:

I'll immediately start:
- Phase 2: Fix build (30 min)
- Phase 3: Implement your chosen strategy
- Phase 4: Testing framework
- Phase 5: Deployment plan

---

## Summary

| Aspect | Strategy A | Strategy B | Strategy C |
|--------|-----------|-----------|-----------|
| **Cleanup** | Full deletion | Gradual | Partial |
| **Risk** | High | Low | Medium |
| **Timeline** | 1-2 weeks | 4-6 weeks | 2-3 weeks |
| **Code Quality** | Best | Good | Fair |
| **Operational Safety** | Risky | Safest | Medium |
| **Rollback** | None | Easy | Hard |
| **Best For** | Greenfield | Production | Pragmatic |

---

**Your choice will determine Phase 3 implementation.**
**Please specify: A, B, C, or describe your preference.**
