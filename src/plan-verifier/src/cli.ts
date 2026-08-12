import { stdin, stdout } from 'node:process'
import type { VerificationTrustedContext } from './validator.js'
import { verifyPlanJson } from './validator.js'

interface VerificationRequest {
  readonly planJson: string
  readonly context: VerificationTrustedContext
}

const chunks: Buffer[] = []
for await (const chunk of stdin) {
  chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk))
}

const request = JSON.parse(Buffer.concat(chunks).toString('utf8')) as
  VerificationRequest
const decision = verifyPlanJson(request.planJson, request.context)
stdout.write(JSON.stringify(decision))
