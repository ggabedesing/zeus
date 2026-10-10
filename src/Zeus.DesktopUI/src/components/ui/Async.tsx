export async function withDeadline<T>(operation: Promise<T>, milliseconds = 30000): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    return await Promise.race([operation, new Promise<never>((_, reject) => {
      timer = setTimeout(() => reject(new Error('A operação demorou mais que o esperado. Confira o Histórico antes de repetir uma alteração.')), milliseconds)
    })])
  } finally { clearTimeout(timer) }
}
