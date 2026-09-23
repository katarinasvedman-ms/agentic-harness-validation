export type Severity = 'critical' | 'high' | 'medium' | 'low'
export type IncidentStatus = 'contained' | 'remediating' | 'resolved'
export type EventKind =
  | 'observation'
  | 'threat-blocked'
  | 'containment'
  | 'recovery'
  | 'plan-verified'
  | 'approval'
  | 'execution'
  | 'evidence'

export interface Incident {
  id: string
  title: string
  severity: Severity
  status: IncidentStatus
  service: string
  startedAt: string
  owner: string
  summary: string
}

export interface AgentEvent {
  id: string
  at: string
  kind: EventKind
  actor: string
  title: string
  detail: string
  outcome: 'safe' | 'blocked' | 'contained' | 'recovering' | 'restored' | 'pending' | 'approved'
}

export interface VerificationCheck {
  id: string
  label: string
  evidence: string
  status: 'passed' | 'blocked'
}

export interface Approval {
  id: string
  status: 'approved' | 'rejected' | 'pending'
  action: string
  target: string
  command: string
  compensation: string
  changeHash: string
  requestedBy: string
  approvedBy: string
  approvedAt: string
  expiresAt: string
  constraints: readonly string[]
}

export interface PolicyState {
  policyVersion: string
  enforcement: 'enforced'
  containmentMode: 'operational' | 'contained' | 'read-only-recovery'
  privileges: readonly string[]
  lastEvaluatedAt: string
  reattestation: {
    artifactDigest: string
    knownGoodVersion: string
    attestedBy: string
  }
  recoverySignOff: {
    actor: string
    rootCause: string
  }
}

export interface AuditEntry {
  sequence: number
  at: string
  event: string
  digest: string
  previousDigest: string
}

export interface IncidentDemo {
  incident: Incident
  events: readonly AgentEvent[]
  checks: readonly VerificationCheck[]
  approval: Approval
  policy: PolicyState
  audit: readonly AuditEntry[]
}
