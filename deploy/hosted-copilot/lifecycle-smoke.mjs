import { spawn } from 'node:child_process'

const executable = process.env.COPILOT_CLI_PATH
if (!executable) {
  throw new Error('COPILOT_CLI_PATH is required')
}

const child = spawn(executable, ['--no-auto-update', '--server', '--stdio'], {
  detached: true,
  env: {
    ...process.env,
    CI: 'true',
    COPILOT_AUTO_UPDATE: 'false',
  },
  stdio: ['pipe', 'pipe', 'pipe'],
})

const launchedPid = await new Promise((resolve, reject) => {
  child.once('spawn', () => resolve(child.pid))
  child.once('error', reject)
})

await new Promise((resolve) => setTimeout(resolve, 750))
if (child.exitCode === null) {
  process.kill(-launchedPid, 'SIGTERM')
}

const result = await Promise.race([
  new Promise((resolve) => child.once('exit', (code, signal) => resolve({ code, signal }))),
  new Promise((_, reject) =>
    setTimeout(() => reject(new Error('Copilot CLI did not stop after cancellation')), 5000),
  ),
])

try {
  process.kill(-launchedPid, 0)
  throw new Error(`Copilot CLI process group ${launchedPid} survived shutdown`)
} catch (error) {
  if (error?.code !== 'ESRCH') {
    throw error
  }
}

console.log(`PASS child lifecycle pid=${launchedPid} exit=${result.code ?? result.signal}`)
