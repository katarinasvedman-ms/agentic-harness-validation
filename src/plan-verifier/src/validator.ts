import { flowRankIsPermitted } from './lattice.js'
import {
  parseActionPlan,
  PlanParseError,
  supportedPlanSchemaVersion,
  type ActionPlan,
  type ApprovalClass,
  type DataClassification,
  type EffectKind,
  type PlanStep,
} from './plan.js'

export type VerificationStatus = 'verified' | 'rejected' | 'indeterminate'

export interface VerificationDecision {
  readonly status: VerificationStatus
  readonly reasonCodes: readonly string[]
  readonly planDigest: string
  readonly specificationVersion: string
  readonly verifierVersion: string
}

export interface TrustedToolMetadata {
  readonly capability: string
  readonly effect: EffectKind
  readonly approvalClass: ApprovalClass
  readonly resourceArgument: string
}

export type ToolRegistry = Readonly<Record<string, TrustedToolMetadata>>

export interface VerificationTrustedContext {
  readonly nowEpochMilliseconds: number
  readonly maximumSteps: number
  readonly agentCapabilities: readonly string[]
  readonly toolRegistry: ToolRegistry
  readonly planDigest: string
  readonly specificationVersion: string
  readonly verifierVersion: string
}

const canonicalIdentifier = /^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$/
const canonicalUuid =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

export function verifyPlanJson(
  json: string,
  context: VerificationTrustedContext,
): VerificationDecision {
  try {
    return verifyPlan(parseActionPlan(json), context)
  } catch (error: unknown) {
    if (error instanceof PlanParseError) {
      return rejected(context, [error.reasonCode])
    }
    throw error
  }
}

export function verifyPlan(
  plan: ActionPlan,
  context: VerificationTrustedContext,
): VerificationDecision {
  const reasons: string[] = []
  validatePlanEnvelope(plan, context, reasons)

  const stepIndexes = new Map<string, number>()
  for (let index = 0; index < plan.steps.length; index += 1) {
    const step = plan.steps[index]
    if (step === undefined) continue
    if (stepIndexes.has(step.stepId)) {
      addReason(reasons, 'duplicate-step-id')
    } else {
      stepIndexes.set(step.stepId, index)
    }
  }

  for (let index = 0; index < plan.steps.length; index += 1) {
    const step = plan.steps[index]
    if (step !== undefined) {
      validateStep(step, index, context, stepIndexes, reasons)
    }
  }

  return reasons.length === 0
    ? {
        status: 'verified',
        reasonCodes: [],
        planDigest: context.planDigest,
        specificationVersion: context.specificationVersion,
        verifierVersion: context.verifierVersion,
      }
    : rejected(context, reasons)
}

export function isVerificationBindingValid(
  decision: VerificationDecision,
  planDigest: string,
  allowedSpecificationVersions: readonly string[],
  allowedVerifierVersions: readonly string[],
): boolean {
  return (
    decision.status === 'verified' &&
    decision.planDigest === planDigest &&
    allowedSpecificationVersions.includes(decision.specificationVersion) &&
    allowedVerifierVersions.includes(decision.verifierVersion)
  )
}

function validatePlanEnvelope(
  plan: ActionPlan,
  context: VerificationTrustedContext,
  reasons: string[],
): void {
  if (plan.schemaVersion !== supportedPlanSchemaVersion) {
    addReason(reasons, 'unsupported-schema')
  }
  if (!canonicalUuid.test(plan.planId)) {
    addReason(reasons, 'invalid-plan-id')
  }
  for (const identifier of [
    plan.incidentId,
    plan.agentId,
    plan.deploymentVersion,
  ]) {
    if (!canonicalIdentifier.test(identifier)) {
      addReason(reasons, 'invalid-identifier')
    }
  }
  if (plan.steps.length < 1 || plan.steps.length > context.maximumSteps) {
    addReason(reasons, 'plan-size-out-of-bounds')
  }

  const createdAt = Date.parse(plan.createdAt)
  const expiresAt = Date.parse(plan.expiresAt)
  if (
    !Number.isFinite(createdAt) ||
    !Number.isFinite(expiresAt) ||
    createdAt > context.nowEpochMilliseconds ||
    expiresAt <= context.nowEpochMilliseconds ||
    createdAt >= expiresAt
  ) {
    addReason(reasons, 'plan-not-current')
  }
}

function validateStep(
  step: PlanStep,
  index: number,
  context: VerificationTrustedContext,
  stepIndexes: ReadonlyMap<string, number>,
  reasons: string[],
): void {
  for (const identifier of [
    step.stepId,
    step.capability,
    step.tool,
    step.resource.type,
    step.resource.id,
    step.destination.id,
  ]) {
    if (!canonicalIdentifier.test(identifier)) {
      addReason(reasons, 'invalid-identifier')
    }
  }

  const trustedTool = context.toolRegistry[step.tool]
  if (trustedTool === undefined) {
    addReason(reasons, 'unknown-tool')
  } else {
    if (
      trustedTool.capability !== step.capability ||
      trustedTool.effect !== step.effect ||
      trustedTool.approvalClass !== step.approvalClass
    ) {
      addReason(reasons, 'tool-metadata-mismatch')
    }
  }
  if (!context.agentCapabilities.includes(step.capability)) {
    addReason(reasons, 'undeclared-capability')
  }

  const seenDependencies = new Set<string>()
  for (const dependency of step.dependsOn) {
    const dependencyIndex = stepIndexes.get(dependency)
    if (
      seenDependencies.has(dependency) ||
      dependencyIndex === undefined ||
      dependencyIndex >= index
    ) {
      addReason(reasons, 'invalid-dependency')
    }
    seenDependencies.add(dependency)
  }

  if (
    step.resource.environment === 'production' &&
    step.effect === 'delete'
  ) {
    addReason(reasons, 'production-delete-prohibited')
  }
  if (
    step.resource.environment === 'production' &&
    step.effect === 'write'
  ) {
    validateCompensation(step, context, reasons)
  }

  for (const source of step.dataSources) {
    if (!canonicalIdentifier.test(source.id)) {
      addReason(reasons, 'invalid-identifier')
    }
    if (!flowRankIsPermitted(
      classificationRank(source.classification),
      classificationRank(step.destination.classification),
    )) {
      addReason(reasons, 'information-flow-prohibited')
    }

    function classificationRank(classification: DataClassification): number {
      if (classification === 'public') return 0
      if (classification === 'internal') return 1
      if (classification === 'internal-trusted') return 2
      if (classification === 'confidential') return 3
      return 4
    }
  }
}

function validateCompensation(
  step: PlanStep,
  context: VerificationTrustedContext,
  reasons: string[],
): void {
  if (step.compensation === null) {
    addReason(reasons, 'compensation-required')
    return
  }

  const compensationTool = context.toolRegistry[step.compensation.tool]
  if (compensationTool === undefined) {
    addReason(reasons, 'compensation-tool-unknown')
    return
  }
  if (!context.agentCapabilities.includes(compensationTool.capability)) {
    addReason(reasons, 'compensation-capability-undeclared')
  }
  if (compensationTool.effect !== 'write') {
    addReason(reasons, 'compensation-must-write')
  }
  if (
    step.compensation.arguments[compensationTool.resourceArgument] !==
    step.resource.id
  ) {
    addReason(reasons, 'compensation-resource-mismatch')
  }
}

function rejected(
  context: VerificationTrustedContext,
  reasonCodes: readonly string[],
): VerificationDecision {
  return {
    status: 'rejected',
    reasonCodes,
    planDigest: context.planDigest,
    specificationVersion: context.specificationVersion,
    verifierVersion: context.verifierVersion,
  }
}

function addReason(reasons: string[], reason: string): void {
  if (!reasons.includes(reason)) {
    reasons.push(reason)
  }
}
