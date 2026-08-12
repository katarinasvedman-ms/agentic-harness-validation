export const supportedPlanSchemaVersion = '1.0' as const

export type EffectKind = 'read' | 'write' | 'delete'
export type TargetEnvironment = 'development' | 'test' | 'production'
export type DataClassification =
  | 'public'
  | 'internal'
  | 'internal-trusted'
  | 'confidential'
  | 'restricted'
export type ApprovalClass =
  | 'none'
  | 'policy-dependent'
  | 'incident-commander'

export interface ResourceReference {
  readonly type: string
  readonly id: string
  readonly environment: TargetEnvironment
  readonly classification: DataClassification
}

export interface DataSourceReference {
  readonly id: string
  readonly classification: DataClassification
}

export interface DestinationReference {
  readonly id: string
  readonly classification: DataClassification
}

export interface CompensationAction {
  readonly tool: string
  readonly arguments: Readonly<Record<string, JsonValue>>
}

export interface PlanStep {
  readonly stepId: string
  readonly capability: string
  readonly tool: string
  readonly resource: ResourceReference
  readonly dataSources: readonly DataSourceReference[]
  readonly destination: DestinationReference
  readonly arguments: Readonly<Record<string, JsonValue>>
  readonly dependsOn: readonly string[]
  readonly effect: EffectKind
  readonly approvalClass: ApprovalClass
  readonly compensation: CompensationAction | null
}

export interface ActionPlan {
  readonly schemaVersion: string
  readonly planId: string
  readonly incidentId: string
  readonly agentId: string
  readonly deploymentVersion: string
  readonly createdAt: string
  readonly expiresAt: string
  readonly steps: readonly PlanStep[]
}

export type JsonValue =
  | null
  | boolean
  | number
  | string
  | JsonValue[]
  | { readonly [key: string]: JsonValue }

const effects = ['read', 'write', 'delete'] as const
const environments = ['development', 'test', 'production'] as const
const classifications = [
  'public',
  'internal',
  'internal-trusted',
  'confidential',
  'restricted',
] as const
const approvalClasses = [
  'none',
  'policy-dependent',
  'incident-commander',
] as const

export class PlanParseError extends Error {
  public constructor(
    public readonly reasonCode: string,
    message: string,
  ) {
    super(message)
    this.name = 'PlanParseError'
  }
}

export function parseActionPlan(json: string): ActionPlan {
  let value: unknown
  try {
    value = JSON.parse(json)
  } catch (error: unknown) {
    if (error instanceof SyntaxError) {
      throw new PlanParseError('malformed-json', 'The plan is not valid JSON.')
    }
    throw error
  }

  const plan = objectWithKeys(value, 'plan', [
    'schemaVersion',
    'planId',
    'incidentId',
    'agentId',
    'deploymentVersion',
    'createdAt',
    'expiresAt',
    'steps',
  ])
  return {
    schemaVersion: stringValue(plan.schemaVersion, 'schemaVersion'),
    planId: stringValue(plan.planId, 'planId'),
    incidentId: stringValue(plan.incidentId, 'incidentId'),
    agentId: stringValue(plan.agentId, 'agentId'),
    deploymentVersion: stringValue(
      plan.deploymentVersion,
      'deploymentVersion',
    ),
    createdAt: stringValue(plan.createdAt, 'createdAt'),
    expiresAt: stringValue(plan.expiresAt, 'expiresAt'),
    steps: arrayValue(plan.steps, 'steps').map(parseStep),
  }
}

function parseStep(value: unknown, index: number): PlanStep {
  const path = `steps[${index}]`
  const step = objectWithKeys(value, path, [
    'stepId',
    'capability',
    'tool',
    'resource',
    'dataSources',
    'destination',
    'arguments',
    'dependsOn',
    'effect',
    'approvalClass',
    'compensation',
  ])
  return {
    stepId: stringValue(step.stepId, `${path}.stepId`),
    capability: stringValue(step.capability, `${path}.capability`),
    tool: stringValue(step.tool, `${path}.tool`),
    resource: parseResource(step.resource, `${path}.resource`),
    dataSources: arrayValue(step.dataSources, `${path}.dataSources`).map(
      (source, sourceIndex) =>
        parseDataSource(source, `${path}.dataSources[${sourceIndex}]`),
    ),
    destination: parseDestination(step.destination, `${path}.destination`),
    arguments: jsonObject(step.arguments, `${path}.arguments`),
    dependsOn: arrayValue(step.dependsOn, `${path}.dependsOn`).map(
      (dependency, dependencyIndex) =>
        stringValue(
          dependency,
          `${path}.dependsOn[${dependencyIndex}]`,
        ),
    ),
    effect: enumValue(step.effect, effects, `${path}.effect`),
    approvalClass: enumValue(
      step.approvalClass,
      approvalClasses,
      `${path}.approvalClass`,
    ),
    compensation:
      step.compensation === null
        ? null
        : parseCompensation(step.compensation, `${path}.compensation`),
  }
}

function parseResource(value: unknown, path: string): ResourceReference {
  const resource = objectWithKeys(value, path, [
    'type',
    'id',
    'environment',
    'classification',
  ])
  return {
    type: stringValue(resource.type, `${path}.type`),
    id: stringValue(resource.id, `${path}.id`),
    environment: enumValue(
      resource.environment,
      environments,
      `${path}.environment`,
    ),
    classification: enumValue(
      resource.classification,
      classifications,
      `${path}.classification`,
    ),
  }
}

function parseDataSource(
  value: unknown,
  path: string,
): DataSourceReference {
  const source = objectWithKeys(value, path, ['id', 'classification'])
  return {
    id: stringValue(source.id, `${path}.id`),
    classification: enumValue(
      source.classification,
      classifications,
      `${path}.classification`,
    ),
  }
}

function parseDestination(
  value: unknown,
  path: string,
): DestinationReference {
  const destination = objectWithKeys(value, path, ['id', 'classification'])
  return {
    id: stringValue(destination.id, `${path}.id`),
    classification: enumValue(
      destination.classification,
      classifications,
      `${path}.classification`,
    ),
  }
}

function parseCompensation(
  value: unknown,
  path: string,
): CompensationAction {
  const compensation = objectWithKeys(value, path, ['tool', 'arguments'])
  return {
    tool: stringValue(compensation.tool, `${path}.tool`),
    arguments: jsonObject(compensation.arguments, `${path}.arguments`),
  }
}

function objectWithKeys(
  value: unknown,
  path: string,
  expectedKeys: readonly string[],
): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new PlanParseError('invalid-type', `${path} must be an object.`)
  }

  const object = value as Record<string, unknown>
  const actualKeys = Object.keys(object)
  const unknownKeys = actualKeys.filter((key) => !expectedKeys.includes(key))
  const missingKeys = expectedKeys.filter(
    (key) => !Object.prototype.hasOwnProperty.call(object, key),
  )
  if (unknownKeys.length > 0) {
    throw new PlanParseError(
      'unknown-field',
      `${path} contains unknown field '${unknownKeys[0]}'.`,
    )
  }
  if (missingKeys.length > 0) {
    throw new PlanParseError(
      'missing-field',
      `${path} is missing field '${missingKeys[0]}'.`,
    )
  }
  return object
}

function jsonObject(
  value: unknown,
  path: string,
): Readonly<Record<string, JsonValue>> {
  if (!isJsonValue(value) || value === null || Array.isArray(value) ||
      typeof value !== 'object') {
    throw new PlanParseError('invalid-type', `${path} must be a JSON object.`)
  }
  return value
}

function isJsonValue(value: unknown): value is JsonValue {
  if (
    value === null ||
    typeof value === 'string' ||
    typeof value === 'boolean'
  ) {
    return true
  }
  if (typeof value === 'number') {
    return Number.isFinite(value)
  }
  if (Array.isArray(value)) {
    return value.every(isJsonValue)
  }
  if (typeof value === 'object') {
    return Object.values(value as Record<string, unknown>).every(isJsonValue)
  }
  return false
}

function arrayValue(value: unknown, path: string): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw new PlanParseError('invalid-type', `${path} must be an array.`)
  }
  return value
}

function stringValue(value: unknown, path: string): string {
  if (typeof value !== 'string') {
    throw new PlanParseError('invalid-type', `${path} must be a string.`)
  }
  return value
}

function enumValue<const T extends string>(
  value: unknown,
  values: readonly T[],
  path: string,
): T {
  const parsed = stringValue(value, path)
  if (!values.includes(parsed as T)) {
    throw new PlanParseError(
      'unknown-enum-value',
      `${path} has unsupported value '${parsed}'.`,
    )
  }
  return parsed as T
}
