import assert from 'node:assert/strict'
import { test } from 'node:test'
import { supportedPlanSchemaVersion } from '@governed-agent/plan-verifier'

test('pins the supported plan schema version', () => {
  assert.equal(supportedPlanSchemaVersion, '1.0')
})
