type Result = Record<string, any>
interface NativeBridge {
  diagnostics(): Promise<Result>
  performance(): Promise<Result>
  history(): Promise<Result[]>
  preview(layoutId: string): Promise<Result>
  apply(previewId: string): Promise<Result>
  revert(transactionId: string): Promise<Result>
}
declare global { interface Window { zeus?: NativeBridge } }
function bridge(): NativeBridge {
  if (!window.zeus) throw new Error('Abra o Zeus PC pelo aplicativo Windows para consultar seu computador. Esta página no navegador mostra apenas a interface.')
  return window.zeus
}
export const native: NativeBridge = {
  diagnostics: async () => bridge().diagnostics(),
  performance: async () => bridge().performance(),
  history: async () => bridge().history(),
  preview: async id => bridge().preview(id),
  apply: async id => bridge().apply(id),
  revert: async id => bridge().revert(id),
}
