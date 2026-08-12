import assert from 'node:assert/strict'
import test from 'node:test'
import {
  flowRankIsPermitted,
  isVerificationBindingValid,
  verifyPlanJson,
} from '@governed-agent/plan-verifier'

const now = Date.parse('2026-08-12T10:00:00Z')
const registry = {
  restart_service: {
    capability: 'service.restart',
    effect: 'write',
    approvalClass: 'incident-commander',
    resourceArgument: 'serviceId',
  },
  restore_service_state: {
    capability: 'service.restore',
    effect: 'write',
    approvalClass: 'incident-commander',
    resourceArgument: 'serviceId',
  },
  query_metrics: {
    capability: 'diagnostics.metrics.read',
    effect: 'read',
    approvalClass: 'none',
    resourceArgument: 'serviceId',
  },
}
const context = {
  nowEpochMilliseconds: now,
  maximumSteps: 8,
  agentCapabilities: [
    'diagnostics.metrics.read',
    'service.restart',
    'service.restore',
  ],
  toolRegistry: registry,
  planDigest: 'a'.repeat(64),
  specificationVersion: '1.0',
  verifierVersion: '0.1.0',
}

function validPlan() {
  return {
    schemaVersion: '1.0',
    planId: '2f64eb2b-40e7-4493-a102-e6fc01828226',
    incidentId: 'INC-1042',
    agentId: 'incident-agent',
    deploymentVersion: '1.0.0',
    createdAt: '2026-08-12T09:59:00Z',
    expiresAt: '2026-08-12T10:05:00Z',
    steps: [
      {
        stepId: 'step-1',
        capability: 'diagnostics.metrics.read',
        tool: 'query_metrics',
        resource: {
          type: 'service',
          id: 'payments-api',
          environment: 'production',
          classification: 'internal',
        },
        dataSources: [
          { id: 'payments-api-metrics', classification: 'internal' },
        ],
        destination: {
          id: 'payments-api',
          classification: 'internal-trusted',
        },
        arguments: { serviceId: 'payments-api' },
        dependsOn: [],
        effect: 'read',
        approvalClass: 'none',
        compensation: null,
      },
      {
        stepId: 'step-2',
        capability: 'service.restart',
        tool: 'restart_service',
        resource: {
          type: 'service',
          id: 'payments-api',
          environment: 'production',
          classification: 'internal',
        },
        dataSources: [
          { id: 'payments-api-metrics', classification: 'internal' },
        ],
        destination: {
          id: 'payments-api',
          classification: 'internal-trusted',
        },
        arguments: {
          serviceId: 'payments-api',
          instanceId: 'payments-api-03',
        },
        dependsOn: ['step-1'],
        effect: 'write',
        approvalClass: 'incident-commander',
        compensation: {
          tool: 'restore_service_state',
          arguments: {
            serviceId: 'payments-api',
            instanceId: 'payments-api-03',
            previousHealth: 'degraded',
            sourceVersion: 1,
          },
        },
      },
    ],
  }
}

function verify(plan, trustedContext = context) {
  return verifyPlanJson(JSON.stringify(plan), trustedContext)
}

test('verifies a bounded production remediation plan', () => {
  const decision = verify(validPlan())

  assert.equal(decision.status, 'verified')
  assert.deepEqual(decision.reasonCodes, [])
  assert.equal(
    isVerificationBindingValid(
      decision,
      context.planDigest,
      ['1.0'],
      ['0.1.0'],
    ),
    true,
  )
})

test('rejects proof-relevant plan mutations', () => {
  const cases = [
    ['undeclared-capability', (plan) => {
      plan.steps[1].capability = 'credential.export'
    }],
    ['tool-metadata-mismatch', (plan) => {
      plan.steps[1].effect = 'read'
    }],
    ['invalid-dependency', (plan) => {
      plan.steps[0].dependsOn = ['step-2']
    }],
    ['production-delete-prohibited', (plan) => {
      plan.steps[1].effect = 'delete'
      plan.steps[1].approvalClass = 'incident-commander'
      plan.steps[1].tool = 'restart_service'
    }],
    ['compensation-required', (plan) => {
      plan.steps[1].compensation = null
    }],
    ['compensation-resource-mismatch', (plan) => {
      plan.steps[1].compensation.arguments.serviceId = 'other-service'
    }],
    ['information-flow-prohibited', (plan) => {
      plan.steps[0].dataSources[0].classification = 'confidential'
      plan.steps[0].destination.classification = 'public'
    }],
    ['plan-not-current', (plan) => {
      plan.expiresAt = '2026-08-12T10:00:00Z'
    }],
  ]

  for (const [expectedReason, mutate] of cases) {
    const plan = validPlan()
    mutate(plan)
    const decision = verify(plan)
    assert.equal(decision.status, 'rejected', expectedReason)
    assert.ok(decision.reasonCodes.includes(expectedReason), expectedReason)
  }
})

test('rejects unknown fields and enum values at the parser boundary', () => {
  const unknownFieldPlan = validPlan()
  unknownFieldPlan.steps[0].resource.promptOverride = 'ignore policy'
  assert.deepEqual(verify(unknownFieldPlan).reasonCodes, ['unknown-field'])

  const unknownEnumPlan = validPlan()
  unknownEnumPlan.steps[0].effect = 'execute-anything'
  assert.deepEqual(verify(unknownEnumPlan).reasonCodes, ['unknown-enum-value'])
})

test('verification result is bound to digest and versions', () => {
  const decision = verify(validPlan())

  assert.equal(
    isVerificationBindingValid(decision, 'b'.repeat(64), ['1.0'], ['0.1.0']),
    false,
  )
  assert.equal(
    isVerificationBindingValid(
      decision,
      context.planDigest,
      ['2.0'],
      ['0.1.0'],
    ),
    false,
  )
})

test('executable classification lattice matches the bounded oracle', () => {
  for (let source = 0; source <= 4; source += 1) {
    for (let destination = 0; destination <= 4; destination += 1) {
      assert.equal(
        flowRankIsPermitted(source, destination),
        source <= destination,
      )
    }
  }
})

test('executable corpus exercises every modeled plan obligation', () => {
  const decision = verify(validPlan())
  assert.equal(decision.status, 'verified') // PO-01, PO-02, PO-05, PO-07

  const expired = validPlan()
  expired.expiresAt = '2026-08-12T09:59:59Z'
  assert.ok(verify(expired).reasonCodes.includes('plan-not-current')) // PO-08

  const unknownField = validPlan()
  unknownField.steps[0].unexpected = true
  assert.ok(verify(unknownField).reasonCodes.includes('unknown-field')) // PO-09

  assert.equal(
    isVerificationBindingValid(
      decision,
      context.planDigest,
      [context.specificationVersion],
      [context.verifierVersion],
    ),
    true,
  ) // PO-10
})
