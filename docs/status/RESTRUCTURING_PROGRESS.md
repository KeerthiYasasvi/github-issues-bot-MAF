# Project Restructuring Progress Report

**Date**: January 20, 2026  
**Status**: IN PROGRESS (Phase 2 Complete)

---

## ✅ COMPLETED

### Phase 1: Directory Structure Created
- ✅ agents/
- ✅ prompts/templates/
- ✅ guardrails/patterns/
- ✅ tools/
- ✅ schemas/json/
- ✅ models/
- ✅ specpack/default/
- ✅ workflows/executors/ (with subfolders: Parse, Setup, Extraction, Scoring, FollowUp, Brief, Routing, Terminal)
- ✅ docs/guides/, docs/diagrams/
- ✅ tests/Agents/, tests/Guardrails/, tests/Tools/, tests/Workflows/, tests/Models/
- ✅ evals/scenarios/judge/
- ✅ artifacts/logs/

### Phase 2: Agents Module Complete
- ✅ agents/ILlmClient.cs (interface + models: LlmMessage, LlmRequest, LlmResponse)
- ✅ agents/ClassifierAgent.cs (updated namespace to SupportConcierge.Agents)
- ✅ agents/ExtractorAgent.cs (updated namespace)
- ✅ agents/FollowUpAgent.cs (added RegenerateAsync method)
- ✅ agents/BriefAgent.cs (unchanged, already has RegenerateAsync)
- ✅ agents/JudgeAgent.cs (updated with enhanced context: issueTitle, category, playbook, requiredFields)
- ✅ agents/OpenAiClient.cs (updated namespace)
- ✅ agents/NullLlmClient.cs (updated namespace)
- ✅ agents/MetricsLlmClient.cs (updated namespace)

### Phase 2b: Prompts Module Complete
- ✅ prompts/Prompts.cs (runtime file loading system)
  - CategoryClassificationAsync()
  - ExtractCasePacketAsync()
  - GenerateFollowUpQuestionsAsync()
  - RegenerateFollowUpQuestionsAsync()
  - GenerateEngineerBriefAsync()
  - RegenerateEngineerBriefAsync()
  - JudgeFollowUpQuestionsAsync()
  - JudgeEngineerBriefAsync()
  - Automatic workspace root detection
  - Fallback template loading (category-specific → default)

- ✅ Prompt Templates Created:
  - prompts/templates/classifier-default.md
  - prompts/templates/extractor-default.md
  - prompts/templates/followup-default.md
  - prompts/templates/followup-regenerate.md
  - prompts/templates/brief-default.md
  - prompts/templates/brief-regenerate.md
  - prompts/templates/judge-followup.md
  - prompts/templates/judge-brief.md

---

## 🚧 REMAINING WORK

### Phase 3: Move Models (7 files)
- [ ] models/RunContext.cs
- [ ] models/BotState.cs
- [ ] models/CaseModels.cs
- [ ] models/EvalModels.cs
- [ ] models/GitHubModels.cs
- [ ] models/JudgeModels.cs
- [ ] models/MetricsModels.cs
- [ ] models/EventInput.cs

### Phase 4: Move Schemas (2 files)
- [ ] schemas/Schemas.cs
- [ ] schemas/json/*.json (schema definitions)

### Phase 5: Move Tools (12 files)
- [ ] tools/IGitHubTool.cs
- [ ] tools/GitHubTool.cs
- [ ] tools/StateStoreTool.cs
- [ ] tools/IssueFormParser.cs
- [ ] tools/SchemaValidator.cs
- [ ] tools/MetricsTool.cs
- [ ] tools/CommentComposer.cs
- [ ] tools/CategoryScorer.cs
- [ ] tools/CompletenessScorer.cs
- [ ] tools/FakeGitHubTool.cs
- [ ] tools/FakeSpecPackLoader.cs
- [ ] tools/ISpecPackLoader.cs

### Phase 6: Move Guardrails (4 files)
- [ ] guardrails/SecretRedactor.cs
- [ ] guardrails/Validators.cs
- [ ] guardrails/CommandParser.cs
- [ ] guardrails/DisagreementDetector.cs
- [ ] guardrails/patterns/*.yaml (pattern definitions)

### Phase 7: Move SpecPack (2 files)
- [ ] specpack/SpecPackLoader.cs
- [ ] specpack/SpecPackModels.cs
- [ ] specpack/default/ (copy from .supportbot/)

### Phase 8: Move Workflows (20 files)
**Root level:**
- [ ] workflows/SupportConciergeWorkflow.cs
- [ ] workflows/SupportConciergeRunner.cs
- [ ] workflows/WorkflowServices.cs
- [ ] workflows/ExecutorDefaults.cs

**Executors (organized by phase):**
- [ ] workflows/executors/Parse/ParseEventExecutor.cs
- [ ] workflows/executors/Setup/LoadSpecPackExecutor.cs
- [ ] workflows/executors/Setup/LoadPriorStateExecutor.cs
- [ ] workflows/executors/Setup/ApplyGuardrailsExecutor.cs
- [ ] workflows/executors/Extraction/ExtractCasePacketExecutor.cs
- [ ] workflows/executors/Scoring/ScoreCompletenessExecutor.cs
- [ ] workflows/executors/FollowUp/GenerateFollowUpsExecutor.cs
- [ ] workflows/executors/FollowUp/ValidateFollowUpsExecutor.cs
- [ ] workflows/executors/FollowUp/PostFollowUpCommentExecutor.cs
- [ ] workflows/executors/Brief/SearchDuplicatesExecutor.cs
- [ ] workflows/executors/Brief/FetchGroundingDocsExecutor.cs
- [ ] workflows/executors/Brief/GenerateEngineerBriefExecutor.cs
- [ ] workflows/executors/Brief/ValidateBriefExecutor.cs
- [ ] workflows/executors/Brief/PostFinalBriefCommentExecutor.cs
- [ ] workflows/executors/Routing/ApplyRoutingExecutor.cs
- [ ] workflows/executors/Terminal/AcknowledgeStopExecutor.cs
- [ ] workflows/executors/Terminal/EscalateExecutor.cs
- [ ] workflows/executors/Terminal/PersistStateExecutor.cs

### Phase 9: Update Project File
- [ ] Update SupportConcierge.Core.csproj to reference new paths
- [ ] Update all namespace imports

### Phase 10: Update Tests (mirror structure)
- [ ] Reorganize existing tests to new structure
- [ ] Add new tests for judge loop
- [ ] Add tests for FollowUpAgent.RegenerateAsync()
- [ ] Add tests for BotState judge fields

### Phase 11: Update Documentation
- [ ] Move docs to docs/guides/ (adding new guides)
- [ ] Create docs/diagrams/ with workflow/judge flow diagrams
- [ ] Create JUDGE_VALIDATION.md guide
- [ ] Update README.md with new structure

### Phase 12: Update CLI & Configuration
- [ ] Update Program.cs imports and paths
- [ ] Update dependency injection wiring
- [ ] Add new environment variables:
  - SUPPORTBOT_ENABLE_JUDGE
  - SUPPORTBOT_MAX_JUDGE_ATTEMPTS
  - SUPPORTBOT_MIN_FOLLOWUP_SCORE
  - SUPPORTBOT_MIN_BRIEF_SCORE

### Phase 13: Validation & Testing
- [ ] Compile solution to verify all imports
- [ ] Run unit tests
- [ ] Manual smoke test with new structure
- [ ] Verify prompt loading works (no recompile changes)

---

## Key Features Implemented

1. **Runtime Prompt Loading**
   - Prompts.cs loads from files at runtime
   - No recompilation needed for prompt changes
   - Category-specific prompts with fallback to defaults
   - Automatic workspace root detection

2. **Agent Namespaces Updated**
   - All agents now use `SupportConcierge.Agents` namespace
   - Ready for integration with other modules

3. **Judge Agent Enhanced**
   - Now accepts: issueTitle, category, playbook, requiredFields
   - Added context-aware scoring prompts
   - Supports follow-up question regeneration

4. **FollowUpAgent Extended**
   - Added RegenerateAsync() method (for judge loop integration)
   - Accepts judge feedback to improve questions

5. **Modular Structure**
   - agents/, prompts/, tools/, guardrails/, schemas/, models/, specpack/, workflows/ fully decoupled
   - Easy to debug/modify individual components
   - Consistent namespace strategy (SupportConcierge.{Module})

---

## Notes for Next Phase

- When moving files, update ALL imports and using statements
- Update SupportConcierge.Core.csproj to include new file paths
- The new namespace scheme removes .Core suffix (e.g., SupportConcierge.Agents not SupportConcierge.Core.Agents)
- All code references to old namespaces must be updated
- Consider creating a migration guide for developers

---

## Estimated Time to Complete All Phases
- Phase 3-7: Move files + update namespaces (~2-3 hours)
- Phase 8: Move/organize executors + update imports (~2-3 hours)
- Phase 9: Update .csproj (~30 min)
- Phase 10: Reorganize tests (~1-2 hours)
- Phase 11: Documentation (~1-2 hours)
- Phase 12: CLI updates (~1 hour)
- Phase 13: Validation (~1-2 hours)

**Total Remaining**: ~10-14 hours

---

## Current Token Usage Estimate
- Already used for planning and Phase 1-2: ~50K tokens
- Remaining phases: ~80-100K tokens estimated
- Total project: ~150K tokens

---

*Document created: January 20, 2026*
