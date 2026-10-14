import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { rcedit } from '../tools/node_modules/rcedit/lib/index.js'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const projectRoot = path.dirname(scriptDir)
const executable = path.join(projectRoot, 'source', 'windows', 'MD Viewer.exe')
const icon = path.join(projectRoot, 'assets', 'plain-icon.ico')

await rcedit(executable, { icon })
console.log(`Replaced executable icon: ${executable}`)
