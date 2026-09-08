# AGENTS.md

Make focused, verifiable changes that fit the existing codebase. Reuse before
creating; minimize unnecessary structure without compromising correctness.

## Scope and Sources of Truth

- Before working on a path, read applicable repository instructions and relevant
  architecture documents. At each directory, prefer the non-empty
  `AGENTS.override.md` over `AGENTS.md`. More specific instructions override
  conflicting ancestor rules; unrelated ancestor rules remain applicable.
- Inspect current files, not remembered repository state. Re-read changed files
  before relying on earlier findings. Report unresolved instruction conflicts.
- The current user request and clarifications define the requested change.
  Approved product documentation defines intended behavior not explicitly changed
  by that request. Implementation is evidence of current behavior, not permission
  to preserve a bug or reject a requested behavior change.
- Verify behavior through production code, configuration, persistence constraints,
  and relevant executable tests. Comments, names, and tests alone do not prove
  that a rule is enforced. Separate verified facts, inference, and open questions.
- Read existing documentation only as relevant: `docs/architecture.md` for layer
  boundaries; `docs/product/domain-model.md`, `business-rules.md`, `workflows.md`,
  and `glossary.md` for approved product behavior and terminology.
- `docs/product/*` describes approved intent; `docs/audits/*` records observations;
  `docs/codex/*` contains working procedures. Report material discrepancies instead
  of silently turning observed behavior into an approved rule.
- Do not create missing documents merely because this file references them.
  If architecture documentation is absent, inspect established code patterns and
  label inferred boundaries as inference.

## Repository-First Implementation

Before designing a solution, inspect the closest existing end-to-end workflow
and search relevant modules for equivalent behavior. Check callers, contracts,
validators, persistence configuration, UI components, and tests as applicable.
Search by behavior and domain terminology, not only by an anticipated class name.

Prefer, in order, when responsibilities and semantics remain compatible:
1. Reuse an existing implementation unchanged.
2. Make a narrow, backward-compatible extension at its established ownership point.
3. Add a small local implementation inside the responsible feature.
4. Introduce a new type, shared abstraction, or infrastructure only when required.

- Use existing domain concepts, names, contracts, services, query helpers, UI
  components, test fixtures, builders, and page objects where they fit.
- Check semantic compatibility, not just similar signatures: authorization,
  ownership, soft delete, validation, error handling, cancellation, and side effects.
- Before changing shared behavior, inspect its current consumers and cover affected
  behavior. Do not silently change other workflows to serve one new caller.
- For each new production file, type, abstraction, or dependency, briefly explain
  its current necessity and why the closest existing alternative is unsuitable.
  Put this in the existing plan or final summary, not a separate design document.
- Do not introduce parallel services, duplicated DTOs, pass-through wrappers,
  generic repositories, helper frameworks, or speculative extension points merely
  to make the solution look cleaner or support hypothetical future requirements.
- A new abstraction may be justified by an actual architectural boundary or current
  integration requirement; do not require an arbitrary number of implementations.
- Add domain entities, tables, or persisted state only when the requested behavior
  needs them. First check whether the existing model already represents the concept.
- Do not force reuse across incompatible responsibilities or layer boundaries,
  expose persistence entities as public contracts, or overload existing types with
  unrelated modes and flags just to avoid creating a necessary type.
- Extend existing test infrastructure before adding another fixture, host, container
  setup, or page-object hierarchy. New focused tests remain expected when needed.

Prefer the smallest complete, maintainable solution, not merely the fewest lines
or files. Follow existing feature structure, but do not maintain a parallel
implementation of behavior already owned elsewhere.

## Workflow and Roles

Choose the smallest applicable workflow:
- Direct explanation, rewriting, or formatting without repository dependence:
  main agent only, no subagents.
- Read-only repository explanation, tracing, documentation, or audit: Research;
  stop unless implementation was also requested.
- Explicit, local, mechanically verifiable changes with no behavior, API,
  persistence, security, or architecture impact: Implementation -> Review.
- Other implementation: Research -> Implementation -> Review.

The full workflow is mandatory for authentication, authorization, ownership,
public APIs, schema/migrations, soft delete/query filters, transactions,
concurrency, idempotency, background jobs/outbox/external effects, token lifecycle,
destructive operations, and sensitive data handling.

The main agent preserves the request and clarifications, selects the workflow,
and may inspect files to classify work, verify findings, and check the result.
When subagent tools are available, it delegates implementation and independent
review; it does not replace independent review with self-review.

Delegated prompts begin with `ROLE: RESEARCH`, `ROLE: IMPLEMENTATION`, or
`ROLE: REVIEW` and include the request, relevant clarifications, scope, constraints,
acceptance criteria, and expected output. Every subagent reads relevant files
directly and completes its own phase without starting nested subagents.

- Research: establish current behavior, reuse candidates, and the necessary plan;
  do not modify production code.
- Implementation: make the scoped change, run relevant checks, and report results.
- Review: independently inspect the request, clarifications, applicable rules,
  actual diff, relevant surrounding code, and verification evidence; do not edit.

If subagents are unavailable, execute the phases sequentially and explicitly
state that review was not independent. Do not claim unavailable capabilities.

## Task Handoff

Reuse an existing user-approved plan or task spec. Validate its repository facts
and adapt only what the request, current evidence, or architecture requires;
do not generate a competing plan merely because a new agent receives the task.

For standard/high-risk work, the handoff must concisely cover:
- Request, verified current behavior, relevant architecture, and non-goals.
- Existing files/symbols to reuse or extend; necessary new artifacts and why.
- Required changes, acceptance criteria, focused tests, risks, and open decisions.

Keep a small handoff in the phase output. Use an existing task note or, when a
file is needed for a complex handoff, `.codex/tasks/<short-kebab-case-name>.md`.
Do not create a spec file automatically for every task or modify `.gitignore`
just for task notes. Do not commit notes without a user request or remove notes
that predate the task.

A plan may intentionally change current behavior when the request requires it.
Reject unsupported claims about current behavior, not the requested difference
between current and desired behavior.

## Working Tree Safety

- Inspect `git status` and the relevant existing diff before editing. Preserve
  pre-existing user changes, including changes inside files you also need to edit.
- Avoid unrelated refactoring, file moves, renaming, and formatting churn.
- Do not run `git reset --hard`, `git clean -fd` or stronger variants, broad
  `git restore .` / `git checkout -- .`, history rewriting, commit amendment,
  or force push without explicit user permission.
- Do not create commits, branches, tags, pull requests, or releases unless asked.
- Change generated files, lock files, migrations, snapshots, or generated clients
  only when required by the task; use the established generation workflow.
- Do not expose secrets, tokens, passwords, certificates, or real connection
  strings. Do not bypass security checks or introduce forbidden dependencies.

## Implementation and Data Integrity

- Keep business rules in their established authoritative layer. UI guards may
  improve usability but must not replace server-side authorization or ownership.
- Preserve cancellation, async behavior, error semantics, and existing contracts
  unless the request requires a change. Avoid placeholders and unexplained TODOs.
- Keep intended database queries server-translatable; materialize only when needed.
  Use `IgnoreQueryFilters()` only in a narrow, explicitly justified path.
- No incidental changes to APIs, authentication/authorization, schema, delete
  semantics, filters, transactions, token lifecycle, or external dependencies.
  Necessary changes require a compatibility/migration assessment and focused checks.
- For affected data-changing workflows, check transaction boundaries, concurrency,
  repeat-call/idempotency behavior, soft delete, notifications/outbox, and failure
  between persistence and external side effects. Do not invent these mechanisms
  when the task does not require them.
- Update touched comments or existing documentation when the requested change
  makes them inaccurate; do not rewrite unrelated documentation.

## Verification and Review

Use repository-provided scripts, CI configuration, and documented commands.
Discover actual solution/project paths, test filters, infrastructure, and EF
startup projects; do not invent them. Start only the documented test dependencies.

- Reuse or extend relevant tests. Changed behavior needs focused coverage, or an
  explicit explanation of the gap and its risk. Bug fixes should cover the failing
  scenario, not merely exercise the modified implementation.
- For test-only requests, do not refactor production code or widen its API/visibility
  for test convenience. Report discovered defects unless fixing them is in scope.
- Start with the affected build/tests; expand to shared consumers and relevant
  integration/end-to-end checks according to risk. Do not rerun the entire suite
  after every small edit unless the repository requires it.
- Run checks against the final changes. Do not skip building modified code unless
  the matching current build has already completed successfully.
- Never weaken assertions, skip failing tests, or relax production behavior merely
  to obtain a passing result. Report pre-existing and new failures separately when
  that distinction is supported by evidence.
- Report exact checks run, failures, unavailable checks with reasons, and any
  substitute static/manual verification. Never claim unperformed tests or reviews.

Review priorities: requested correctness; authorization/ownership/data exposure;
data integrity/concurrency; compatibility; errors/cancellation; architecture and
reuse; test validity; maintainability; comments/style.

Review must inspect new artifacts and shared-code changes: is each necessary,
is equivalent behavior already available, and do existing consumers still work?
Unnecessary production abstractions that violate the reuse rules are findings,
not automatically acceptable because tests pass. Explain the concrete duplication
or violated constraint; a mere preference for fewer files is not a blocker.

Return:
- `PASS`: no blocking findings; summarize actual verification and non-blocking risks.
- `FAIL`: for each finding give severity (Critical/Major/Minor), file/symbol,
  observable problem or violated requirement, and required correction.
- `BLOCKED`: a required decision, permission, or evidence is unavailable; explain
  what was attempted, why it matters, and available options.

Do not fail an implementation for optional style preferences. Missing evidence
needed to establish a critical acceptance criterion is `BLOCKED`, not `PASS` with
an unqualified completion claim. Optional unavailable checks may be reported as
non-blocking limitations with their risk explained.

After `FAIL`, run Implementation -> Review, with at most two automatic repair
cycles. Stop earlier if the same blocker recurs, architecture conflicts with the
plan, or repair needs a product decision, missing permission, or a scope change.
Continue safe independent work; ask only for decisions that genuinely block it.

## Comments and LINQ Style

Explain non-obvious intent, invariants, ordering, security boundaries, race
prevention, cache choices, workarounds, and failure paths. Do not narrate trivial
syntax or every line. Follow the existing public-API XML documentation policy.

In integration, end-to-end, and regression tests, explain meaningful arrange/act/
assert phases. Give distinct regression conditions separate short comments before
their assertions; do not hide different regression purposes under a generic comment.

Use fluent method-chain syntax for new or materially modified LINQ/EF queries.
Do not introduce query-expression syntax or rewrite unrelated queries for style.

## Business-Rule Analysis

For rule extraction/audits, read `docs/codex/business-rules-audit.md` when present.
Trace the relevant complete server path, persistence constraints, filters, helper
call sites, side effects, UI guards, and tests. A declared helper is not an enforced
rule until the enforcing call sites are verified.

Classify findings as server-enforced, database-enforced, UI-only, declared but
unproven, inferred, contradictory, or unknown. Cite file paths and symbols;
separate unresolved product decisions. Never silently turn a suspected bug into
an approved business rule.

## Completion and Final Response

Complete means: the request and acceptance criteria are satisfied, the selected
workflow is complete, the diff is scoped, required verification is sufficient,
and no blocking issue remains. Distinguish implementation finished from verification
finished; disclose non-blocking limitations without claiming stronger assurance.

Briefly report the result, changed files, significant reuse, necessary new artifacts
and their justification, checks performed, unverified behavior, and remaining risks.
Do not create a separate report file unless requested or genuinely needed.
