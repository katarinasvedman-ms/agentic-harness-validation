datatype Effect = Read | Write | Delete
datatype Environment = Development | Test | Production
datatype StepState =
  Proposed | Verified | AwaitingApproval | Approved | Executing |
  Completed | Denied | Failed | Compensating | Compensated

datatype Step = Step(
  capability: string,
  registeredCapability: string,
  effect: Effect,
  registeredEffect: Effect,
  environment: Environment,
  index: nat,
  dependencies: seq<nat>,
  sourceRank: nat,
  destinationRank: nat,
  hasBoundedCompensation: bool)

datatype Approval = Approval(
  planId: string,
  stepId: string,
  actionDigest: string,
  roleMatches: bool,
  issuedAt: int,
  expiresAt: int,
  consumed: bool,
  revoked: bool)

datatype VerificationBinding = VerificationBinding(
  planDigest: string,
  specificationVersion: string,
  verifierVersion: string)

predicate DependencySafe(step: Step)
{
  forall dependency :: dependency in step.dependencies ==>
    dependency < step.index
}

predicate CapabilitySafe(step: Step, declaredCapabilities: set<string>)
{
  step.capability in declaredCapabilities &&
  step.capability == step.registeredCapability &&
  step.effect == step.registeredEffect
}

predicate ProductionEffectSafe(step: Step)
{
  !(step.environment == Production && step.effect == Delete)
}

predicate InformationFlowSafe(step: Step)
{
  step.sourceRank <= 4 &&
  step.destinationRank <= 4 &&
  step.sourceRank <= step.destinationRank
}

predicate CompensationSafe(step: Step)
{
  step.environment == Production && step.effect == Write ==>
    step.hasBoundedCompensation
}

predicate StepSafe(step: Step, declaredCapabilities: set<string>)
{
  CapabilitySafe(step, declaredCapabilities) &&
  DependencySafe(step) &&
  ProductionEffectSafe(step) &&
  InformationFlowSafe(step) &&
  CompensationSafe(step)
}

predicate ValidatorAccepts(
  steps: seq<Step>,
  declaredCapabilities: set<string>)
{
  |steps| > 0 &&
  forall index :: 0 <= index < |steps| ==>
    steps[index].index == index &&
    StepSafe(steps[index], declaredCapabilities)
}

lemma PO_01_AcceptanceImpliesCoreInvariants(
  steps: seq<Step>,
  declaredCapabilities: set<string>)
  requires ValidatorAccepts(steps, declaredCapabilities)
  ensures forall index :: 0 <= index < |steps| ==>
    CapabilitySafe(steps[index], declaredCapabilities) &&
    DependencySafe(steps[index]) &&
    ProductionEffectSafe(steps[index])
{
}

lemma PO_02_OrderedDependenciesAreAcyclic(
  steps: seq<Step>,
  declaredCapabilities: set<string>)
  requires ValidatorAccepts(steps, declaredCapabilities)
  ensures forall index, dependency ::
    0 <= index < |steps| &&
    dependency in steps[index].dependencies ==>
      dependency < index
{
}

predicate ValidApproval(
  approval: Approval,
  expectedPlanId: string,
  expectedStepId: string,
  expectedActionDigest: string,
  now: int)
{
  approval.planId == expectedPlanId &&
  approval.stepId == expectedStepId &&
  approval.actionDigest == expectedActionDigest &&
  approval.roleMatches &&
  approval.issuedAt <= now < approval.expiresAt &&
  !approval.consumed &&
  !approval.revoked
}

lemma PO_03_ApprovalIsExactAndCurrent(
  approval: Approval,
  expectedPlanId: string,
  expectedStepId: string,
  expectedActionDigest: string,
  now: int)
  requires ValidApproval(
    approval,
    expectedPlanId,
    expectedStepId,
    expectedActionDigest,
    now)
  ensures approval.planId == expectedPlanId
  ensures approval.stepId == expectedStepId
  ensures approval.actionDigest == expectedActionDigest
  ensures approval.issuedAt <= now < approval.expiresAt
  ensures !approval.consumed && !approval.revoked
{
}

predicate PermittedTransition(
  current: StepState,
  next: StepState,
  killSwitchActive: bool,
  approvalValid: bool,
  idempotencyKeyAlreadyExecuted: bool)
{
  !killSwitchActive &&
  !(current == Denied &&
    (next == Approved || next == Executing || next == Completed)) &&
  !(current == Completed && next == Executing) &&
  !(idempotencyKeyAlreadyExecuted && next == Executing) &&
  (next == Executing ==> approvalValid)
}

lemma PO_04_RuntimeTransitionsPreserveSafety(
  current: StepState,
  next: StepState,
  killSwitchActive: bool,
  approvalValid: bool,
  idempotencyKeyAlreadyExecuted: bool)
  requires PermittedTransition(
    current,
    next,
    killSwitchActive,
    approvalValid,
    idempotencyKeyAlreadyExecuted)
  ensures current == Denied ==>
    next != Approved && next != Executing && next != Completed
  ensures idempotencyKeyAlreadyExecuted ==> next != Executing
  ensures killSwitchActive ==> next != Executing
{
}

lemma PO_05_AcceptedFlowsRespectClassification(
  steps: seq<Step>,
  declaredCapabilities: set<string>)
  requires ValidatorAccepts(steps, declaredCapabilities)
  ensures forall index :: 0 <= index < |steps| ==>
    steps[index].sourceRank <= steps[index].destinationRank
{
}

lemma PO_06_DelegationCannotEscalate(
  parentCapabilities: set<string>,
  delegatedCapabilities: set<string>,
  childCapabilities: set<string>)
  requires childCapabilities <=
    parentCapabilities * delegatedCapabilities
  ensures childCapabilities <= parentCapabilities
  ensures childCapabilities <= delegatedCapabilities
{
}

lemma PO_07_ProductionWritesDeclareCompensation(
  steps: seq<Step>,
  declaredCapabilities: set<string>)
  requires ValidatorAccepts(steps, declaredCapabilities)
  ensures forall index :: 0 <= index < |steps| ==>
    steps[index].environment == Production &&
    steps[index].effect == Write ==>
      steps[index].hasBoundedCompensation
{
}

predicate PlanIsCurrent(createdAt: int, expiresAt: int, now: int)
{
  createdAt <= now < expiresAt
}

lemma PO_08_ExpiredPlansCannotExecute(
  createdAt: int,
  expiresAt: int,
  now: int,
  next: StepState)
  requires !PlanIsCurrent(createdAt, expiresAt, now)
  requires next == Verified || next == Approved || next == Executing ==>
    PlanIsCurrent(createdAt, expiresAt, now)
  ensures next != Verified && next != Approved && next != Executing
{
}

predicate ParserAccepts(
  knownSchema: bool,
  hasUnknownFields: bool,
  allEnumsKnown: bool)
{
  knownSchema && !hasUnknownFields && allEnumsKnown
}

lemma PO_09_ParserIsClosed(
  knownSchema: bool,
  hasUnknownFields: bool,
  allEnumsKnown: bool)
  requires ParserAccepts(knownSchema, hasUnknownFields, allEnumsKnown)
  ensures knownSchema
  ensures !hasUnknownFields
  ensures allEnumsKnown
{
}

predicate VerificationApplies(
  binding: VerificationBinding,
  expectedPlanDigest: string,
  allowedSpecificationVersions: set<string>,
  allowedVerifierVersions: set<string>)
{
  binding.planDigest == expectedPlanDigest &&
  binding.specificationVersion in allowedSpecificationVersions &&
  binding.verifierVersion in allowedVerifierVersions
}

lemma PO_10_VerificationIsBound(
  binding: VerificationBinding,
  expectedPlanDigest: string,
  allowedSpecificationVersions: set<string>,
  allowedVerifierVersions: set<string>)
  requires VerificationApplies(
    binding,
    expectedPlanDigest,
    allowedSpecificationVersions,
    allowedVerifierVersions)
  ensures binding.planDigest == expectedPlanDigest
  ensures binding.specificationVersion in allowedSpecificationVersions
  ensures binding.verifierVersion in allowedVerifierVersions
{
}
