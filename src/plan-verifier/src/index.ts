export const supportedPlanSchemaVersion = '1.0' as const

export type VerificationStatus = 'verified' | 'rejected' | 'indeterminate'

export interface VerificationDecision {
  readonly status: VerificationStatus
  readonly reasonCodes: readonly string[]
}
