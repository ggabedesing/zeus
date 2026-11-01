import { useEffect, useRef, useState } from 'react'
import { Activity, ArrowRight, ArrowUpRight, Check, CheckCircle2, ChevronDown, CircleHelp, Cpu, Download, Gamepad2, HardDrive, History, LoaderCircle, MemoryStick, Monitor, RotateCcw, ScanLine, Sparkles, Trash2, X, Zap } from 'lucide-react'
import type { Adjustment, Report, Run, Snapshot } from './types'

const goals = [{ id: 'daily', title: 'Dia a dia', icon: Monitor }, { id: 'work', title: 'Trabalho pesado', icon: Cpu }, { id: 'gaming', title: 'Jogos', icon: Gamepad2 }]
const localHost = ['127.0.0.1', 'localhost', '[::1]'].includes(window.location.hostname)
const repo = 'https://github.com/ggabedesing/zeus-pc/tree/codex/zeus-otimizacao'
function bytes(n: number) {
  if (n >= 1024 ** 3) return `${(n / 1024 ** 3).toFixed(1).replace('.', ',')} GB`
  if (n >= 1024 ** 2) return `${Math.round(n / 1024 ** 2)} MB`
  return `${Math.round(n / 1024)} KB`
}
function number(n: number | null | undefined, unit = '%') { return n == null ? '—' : `${Math.round(n)}${unit}` }
async function api<T>(path: string, body?: unknown): Promise<T> {
  const controller = new AbortController()
  const timer = window.setTimeout(() => controller.abort(), path.endsWith('/apply') || path.endsWith('/undo') ? 180000 : 30000)
  try {
    const response = await fetch(path, { signal: controller.signal, ...(body !== undefined ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}) })
    const value = await response.json()
    if (!response.ok) throw new Error(value.message || value.error || 'O Zeus não conseguiu concluir esta etapa.')
    return value
  } finally { window.clearTimeout(timer) }
}
function Metric({ icon: Icon, label, value, detail, amount }: { icon: typeof Cpu; label: string; value: string; detail: string; amount?: number | null }) {
  return <article className="metric"><div className="metric-heading"><Icon size={17} /><span>{label}</span></div><strong>{value}</strong><p>{detail}</p><div className="meter" aria-hidden="true"><span style={{ width: `${Math.max(0, Math.min(100, amount ?? 0))}%` }} /></div></article>
}
function AdjustmentRow({ action, checked, disabled, onChange }: { action: Adjustment; checked: boolean; disabled: boolean; onChange: () => void }) {
  const Icon = action.id === 'cleanup' ? Trash2 : action.category === 'startup' ? Monitor : action.id === 'energy' ? Zap : Sparkles
  return <label className={`adjustment ${checked ? 'selected' : ''}`}><input type="checkbox" checked={checked} disabled={disabled} onChange={onChange} /><span className="checkbox" aria-hidden="true">{checked && <Check size={13} strokeWidth={3} />}</span><span className="adjustment-icon"><Icon size={20} /></span><span className="adjustment-copy"><strong>{action.title}</strong><span>{action.description}</span><small>{action.tradeoff}</small></span><span className={`tag ${action.reversible === false ? 'amber' : ''}`}>{action.bytes ? bytes(action.bytes) : action.category === 'startup' ? 'Opcional' : 'Reversível'}</span></label>
}

export default function App() {
  const [connected, setConnected] = useState<boolean | null>(null)
  const [goal, setGoal] = useState('gaming')
  const [report, setReport] = useState<Report | null>(null)
  const [runs, setRuns] = useState<Run[]>([])
  const [run, setRun] = useState<Run | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [view, setView] = useState('optimize')
  const [help, setHelp] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [showStartup, setShowStartup] = useState(false)
  const confirmRef = useRef<HTMLDialogElement>(null)
  const helpRef = useRef<HTMLDialogElement>(null)
  const panel = useRef<HTMLElement>(null)
  const initDone = useRef(false)

  function loadReport(next: Report | null) {
    setReport(next)
    if (next) { setGoal(next.goal); setSelected(next.actions.filter(a => a.default !== false).map(a => a.id)) }
  }
  async function refresh() {
    const value = await api<{ report: Report | null; runs: Run[] }>('/api/focus/state')
    setRuns(value.runs)
    return value
  }
  useEffect(() => {
    if (initDone.current) return
    initDone.current = true
    if (!localHost) { setConnected(false); return }
    refresh().then(value => { setConnected(true); loadReport(value.report); const latest = value.runs[0]; if (latest && value.report?.id === latest.id) setRun(latest) }).catch(() => setConnected(false))
  }, [])
  useEffect(() => { if (confirm) confirmRef.current?.showModal(); else confirmRef.current?.close() }, [confirm])
  useEffect(() => { if (help) helpRef.current?.showModal(); else helpRef.current?.close() }, [help])

  async function analyze() {
    setError(''); setBusy('analyze')
    try {
      const value = await api<{ report: Report }>('/api/focus/analyze', { goal })
      loadReport(value.report); setRun(null); setView('optimize')
      window.setTimeout(() => panel.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 80)
    } catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível analisar. Tente novamente.') }
    finally { setBusy('') }
  }
  async function apply() {
    if (!report) return
    setConfirm(false); setError(''); setBusy('apply')
    try { const value = await api<{ run: Run }>('/api/focus/apply', { report_id: report.id, action_ids: selected }); setRun(value.run); await refresh() }
    catch (e) {
      setError(e instanceof Error && e.name !== 'AbortError' ? e.message : 'A resposta foi interrompida. Confira o histórico antes de tentar novamente.')
      try { const state = await refresh(); const existing = state.runs.find(r => r.id === report.id); if (existing) setRun(existing) } catch { /* Keep last known result. */ }
    } finally { setBusy('') }
  }
  async function undo(item: Run) {
    setBusy(`undo-${item.id}`); setError('')
    try {
      const value = await api<{ run: Run }>('/api/focus/undo', { report_id: item.id })
      setRun(value.run); await refresh()
      const failures = value.run.undo_results?.filter(r => !r.ok)
      if (!value.run.undone && failures?.length) setError(failures[failures.length - 1].message || 'Alguns ajustes não puderam ser restaurados.')
    } catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível restaurar.') }
    finally { setBusy('') }
  }
  async function openTarget(target: string) {
    try { await api('/api/focus/open', { target }) }
    catch (e) { setError(e instanceof Error ? e.message : 'Não foi possível abrir o Windows.') }
  }
  const snapshot: Snapshot | undefined = run?.after || report?.snapshot
  const coreActions = report?.actions.filter(a => a.category !== 'startup') || []
  const startupActions = report?.actions.filter(a => a.category === 'startup') || []
  const selectedActions = report?.actions.filter(a => selected.includes(a.id)) || []
  const removed = run?.results.reduce((n, r) => n + (r.freed_bytes || 0), 0) || 0
  const canUndo = (item: Run) => !item.undone && item.results.some(r => r.ok && r.transaction_id)
  const date = (stamp: number) => new Date(stamp * 1000).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
  const toggle = (id: string) => setSelected(list => list.includes(id) ? list.filter(i => i !== id) : [...list, id])

  return <div className="app-shell"><a className="skip-link" href="#main">Ir para o conteúdo</a>
    <header className="topbar"><a href="/" className="brand" aria-label="Zeus, início"><span className="brand-mark"><Zap size={20} fill="currentColor" /></span>zeus<span className="brand-dot">.</span></a><nav aria-label="Navegação principal"><button className={view === 'optimize' ? 'active' : ''} onClick={() => setView('optimize')}><Zap size={15} />Otimizar</button><button className={view === 'history' ? 'active' : ''} onClick={() => setView('history')}><History size={15} />Histórico{runs.length > 0 && <span className="nav-count">{runs.length}</span>}</button></nav><div className="connection"><span className={connected ? 'dot live' : 'dot'} /><span>{connected == null ? 'Conectando' : connected ? 'No seu computador' : 'Para Windows'}</span></div><button className="help-button" onClick={() => setHelp(true)} aria-label="Como o Zeus funciona"><CircleHelp size={20} /></button></header>
    <main id="main">{error && <div className="error-banner" role="alert"><span>{error}</span><button onClick={() => setError('')} aria-label="Fechar mensagem"><X size={17} /></button></div>}
      {view === 'optimize' ? <>
        <section className="hero" aria-labelledby="hero-title"><div className="hero-copy"><div className="eyebrow"><span />MENOS EXCESSO. MAIS DESEMPENHO.</div><h1 id="hero-title">Seu PC.<br /><span>Sem peso morto.</span></h1><p className="hero-description">Libere espaço. Tire o excesso da inicialização.<br className="desktop-break" /> Prepare o Windows para o que você quer fazer.</p><div className="goal-label" id="goal-label">O que você vai fazer agora?</div><div className="goals" role="group" aria-labelledby="goal-label">{goals.map(({ id, title, icon: Icon }) => <button disabled={!!busy} key={id} aria-pressed={goal === id} className={goal === id ? 'selected' : ''} onClick={() => { setGoal(id); if (report && report.goal !== id) { setReport(null); setRun(null) } }}><Icon size={16} />{title}</button>)}</div>
          {connected ? <button className="primary hero-cta" disabled={!!busy} onClick={analyze}>{busy === 'analyze' ? <LoaderCircle size={19} className="spin" /> : <ScanLine size={19} />}{busy === 'analyze' ? 'Encontrando o que pesa…' : report ? 'Analisar novamente' : 'Analisar meu PC'}{busy !== 'analyze' && <ArrowRight size={18} />}</button> : <a className="primary hero-cta" href={localHost ? 'http://127.0.0.1:8000' : repo} target={localHost ? undefined : '_blank'} rel="noreferrer"><Download size={18} />{connected == null ? 'Conectando ao Zeus…' : localHost ? 'Abrir Zeus local' : 'Obter Zeus para Windows'}<ArrowUpRight size={17} /></a>}<div className="hero-note"><span className="mini-check"><Check size={10} strokeWidth={3} /></span>{connected ? 'Analisar só faz leituras. Você escolhe o que aplicar.' : 'Otimização local. Seus dados ficam no seu computador.'}</div></div>
          <div className={`visual ${busy ? 'working' : ''}`} aria-hidden="true"><div className="visual-grid" /><div className="orbit orbit-one" /><div className="orbit orbit-two" /><div className="orbit orbit-three" /><div className="cross c1" /><div className="cross c2" /><div className="visual-caption">ZEUS / PERFORMANCE ENGINE</div><div className="core"><Zap strokeWidth={1.1} fill="currentColor" /></div><div className="visual-label label-one"><span className="dot live" />{run?.status === 'verified' ? 'AJUSTES CONFIRMADOS' : busy ? 'ANÁLISE EM ANDAMENTO' : 'PRONTO PARA AGIR'}</div><div className="visual-label label-two"><Activity size={14} />LOCAL · DIRETO · REAL</div></div></section>
        <section className="overview" aria-label="Estado do computador"><div className="section-kicker"><span>{snapshot ? snapshot.computer || 'SEU COMPUTADOR' : 'SEU COMPUTADOR, SEM ADIVINHAÇÃO'}</span><span className="reading-label"><span className="dot" />{snapshot ? 'Leitura da última análise' : 'Os números aparecem após a análise'}</span></div><div className="metrics"><Metric icon={Cpu} label="Processador" value={number(snapshot?.cpu_pct)} detail={snapshot?.cpu_name || 'Uso real de processamento'} amount={snapshot?.cpu_pct} /><Metric icon={MemoryStick} label="Memória em uso" value={number(snapshot?.ram_used_pct)} detail={snapshot?.ram_available_gb != null ? `${snapshot.ram_available_gb} GB disponíveis de ${snapshot.ram_total_gb} GB` : 'Consumo real de memória'} amount={snapshot?.ram_used_pct} /><Metric icon={HardDrive} label="Espaço disponível" value={number(snapshot?.disk_free_gb, ' GB')} detail={snapshot?.disk_total_gb ? `No disco do Windows · ${snapshot.disk_total_gb} GB no total` : 'Espaço livre no disco do Windows'} amount={snapshot?.disk_free_pct} /></div></section>
        <section ref={panel} className="action-section" aria-labelledby="actions-title"><div className="section-heading"><div><div className="eyebrow small">{run ? 'RESULTADO DA EXECUÇÃO' : report ? 'SEU PLANO DE OTIMIZAÇÃO' : 'UMA ANÁLISE. UM PLANO. AÇÃO.'}</div><h2 id="actions-title">{run ? run.undone ? 'Ajustes restaurados.' : run.status === 'verified' ? 'Aplicado. Conferido.' : 'Confira o que aconteceu.' : report ? report.actions.length ? 'Aqui dá para agir.' : 'Tudo ajustado por aqui.' : 'O que está pesando no seu PC?'}</h2></div>{report && !run && <span className="scan-time"><CheckCircle2 size={14} />{date(report.created_at)}</span>}</div>
          {busy === 'apply' ? <div className="processing" role="status"><LoaderCircle size={36} className="spin" /><h3>O Zeus está trabalhando.</h3><p>Aplicando os ajustes escolhidos e relendo o estado do Windows.</p><div className="indeterminate"><span /></div></div>
          : run ? <div className="result-panel"><div className="result-top"><div className="result-symbol"><CheckCircle2 size={28} /></div><div><h3>{run.undone ? 'Suas configurações anteriores estão de volta.' : run.status === 'verified' ? `${run.results.filter(r => r.ok).length} ajustes executados` : run.status === 'running' ? 'A execução foi interrompida ou continua em andamento.' : 'Uma parte do plano precisa de atenção.'}</h3><p>{run.undone ? 'A limpeza de temporários permanece feita.' : removed > 0 ? `${bytes(removed)} de arquivos temporários removidos.` : 'O resultado de cada ação aparece abaixo.'}</p></div></div><div className="result-rows">{run.results.map((r, index) => <div className="result-row" key={index}>{r.ok ? <CheckCircle2 size={17} /> : <CircleHelp size={17} />}<span><strong>{r.title}</strong><small>{r.message || 'Confira o estado antes de continuar.'}</small></span><span className={`tag ${r.ok ? '' : 'amber'}`}>{run.undone && r.transaction_id ? 'Restaurado' : r.ok ? 'Confirmado' : 'Atenção'}</span></div>)}</div>{run.after && <div className="before-after"><span><span>Espaço livre no disco</span><strong>{number(run.before.disk_free_gb, ' GB')} <ArrowRight size={14} /> {number(run.after.disk_free_gb, ' GB')}</strong></span><small>Leituras antes e depois da execução. Ganhos de FPS exigem um teste no seu jogo.</small></div>}<div className="result-footer"><button className="secondary" disabled={!!busy} onClick={analyze}><ScanLine size={16} />Nova análise</button>{canUndo(run) && <button className="text-button" disabled={!!busy} onClick={() => undo(run)}><RotateCcw size={15} />{busy.startsWith('undo-') ? 'Restaurando…' : 'Desfazer ajustes reversíveis'}</button>}</div></div>
          : report ? <>{report.status === 'unsupported' && <div className="empty-panel"><Monitor size={32} /><h3>O motor do Zeus precisa do Windows.</h3><p>Abra o aplicativo no computador que você quer otimizar.</p></div>}{report.status !== 'unsupported' && <><div className="adjustments">{coreActions.map(action => <AdjustmentRow key={action.id} action={action} checked={selected.includes(action.id)} disabled={!!busy} onChange={() => toggle(action.id)} />)}</div>{startupActions.length > 0 && <div className="startup-block"><button className="startup-toggle" aria-expanded={showStartup} onClick={() => setShowStartup(!showStartup)}><span><Monitor size={18} /><strong>Enxugar a inicialização</strong><span>{startupActions.length} aplicativos</span></span><ChevronDown size={18} className={showStartup ? 'flipped' : ''} /></button><p>Escolha o que pode esperar para abrir. Nada é desinstalado.</p>{showStartup && <div className="adjustments">{startupActions.map(action => <AdjustmentRow key={action.id} action={action} checked={selected.includes(action.id)} disabled={!!busy} onChange={() => toggle(action.id)} />)}</div>}</div>}{report.actions.length === 0 && <div className="empty-panel"><CheckCircle2 size={34} /><h3>Os ajustes disponíveis já estão feitos.</h3><p>O Zeus só propõe o que encontrou no seu Windows.</p></div>}{report.actions.length > 0 && <div className="apply-bar"><span><strong>{selected.length} {selected.length === 1 ? 'ajuste selecionado' : 'ajustes selecionados'}</strong><small>{selectedActions.some(a => a.reversible === false) ? 'Inclui exclusão de arquivos temporários.' : 'Os ajustes selecionados podem ser desfeitos.'}</small></span><button className="primary" disabled={!selected.length || !!busy} onClick={() => setConfirm(true)}><Zap size={18} fill="currentColor" />Otimizar agora<ArrowRight size={17} /></button></div>}</>}{report.notices.length > 0 && <div className="notices">{report.notices.map(n => <article key={n.id}><CircleHelp size={19} /><div><h3>{n.title}</h3><p>{n.description}</p><button className="text-button" onClick={() => openTarget(n.uri)}>{n.label}<ArrowUpRight size={14} /></button></div></article>)}</div>}{!!report.snapshot.read_errors?.length && <details className="read-errors"><summary>Algumas leituras ficaram indisponíveis</summary>{report.snapshot.read_errors.map((e, i) => <p key={i}>{e}</p>)}</details>}</>
          : <div className="preflight"><div className="preflight-icon"><ScanLine size={27} /></div><div><h3>{busy === 'analyze' ? 'Buscando oportunidades reais…' : 'Comece pelo que o seu computador precisa.'}</h3><p>{busy === 'analyze' ? 'Lendo energia, memória, espaço, temporários e inicialização.' : 'O Zeus encontra os excessos e prepara os ajustes em um só lugar.'}</p></div><ArrowRight className="preflight-arrow" size={20} /></div>}
        </section><section className="principles" aria-label="Como funciona"><article><span>01</span><h3>Encontra o excesso.</h3><p>Temporários esquecidos, programas na partida e configurações de desempenho.</p></article><article><span>02</span><h3>Faz o trabalho.</h3><p>Você escolhe os ajustes. O Zeus executa diretamente no Windows.</p></article><article><span>03</span><h3>Mostra o resultado.</h3><p>Arquivos removidos e configurações confirmadas, ação por ação.</p></article></section>
      </> : <section className="history-page"><div className="eyebrow">O QUE O ZEUS FEZ</div><h1>Sem caixa-preta.</h1><p className="hero-description">Cada execução fica registrada neste computador.</p>{runs.length === 0 ? <div className="empty-panel"><History size={32} /><h3>Seu histórico começa na primeira otimização.</h3><button className="secondary" onClick={() => setView('optimize')}>Ir para otimização<ArrowRight size={16} /></button></div> : runs.map(item => <article className="history-card" key={item.id}><div className="history-top"><div><span className="eyebrow small">{date(item.created_at)}</span><h3>{item.undone ? 'Ajustes restaurados' : item.status === 'verified' ? 'Otimização concluída' : 'Execução com pendências'}</h3></div><span className="tag">{goals.find(g => g.id === item.goal)?.title || 'Personalizado'}</span></div>{item.results.map((r, i) => <div className="history-item" key={i}>{r.ok ? <Check size={15} /> : <CircleHelp size={15} />}<span>{r.title}</span><small>{r.freed_bytes ? bytes(r.freed_bytes) : r.ok ? 'Confirmado' : 'Conferir'}</small></div>)}{canUndo(item) && <button className="text-button" disabled={!!busy} onClick={() => undo(item)}><RotateCcw size={15} />{busy === `undo-${item.id}` ? 'Restaurando…' : 'Desfazer ajustes reversíveis'}</button>}</article>)}</section>}
    </main><footer><a href="/" className="footer-brand">zeus.</a><span>Um objetivo. Seu PC rendendo mais.</span><span className="footer-right">Feito para agir.<span className="dot live" /></span></footer>
    <dialog ref={confirmRef} className="modal" onCancel={() => setConfirm(false)} onClick={e => { if (e.target === e.currentTarget) setConfirm(false) }}><button className="modal-close" onClick={() => setConfirm(false)} aria-label="Fechar"><X size={19} /></button><span className="modal-icon"><Zap size={25} /></span><div className="eyebrow small">PRONTO PARA EXECUTAR</div><h2>Vamos tirar esse peso.</h2><p>O Zeus vai executar os {selected.length} ajustes que você escolheu:</p><ul>{selectedActions.map(a => <li key={a.id}><Check size={15} /><span>{a.title}{a.reversible === false && <small>Os arquivos temporários serão excluídos permanentemente.</small>}</span></li>)}</ul><div className="modal-actions"><button className="secondary" onClick={() => setConfirm(false)}>Voltar</button><button className="primary" onClick={apply}>Aplicar {selected.length} ajustes<ArrowRight size={17} /></button></div></dialog>
    <dialog ref={helpRef} className="modal" onCancel={() => setHelp(false)}><button className="modal-close" onClick={() => setHelp(false)} aria-label="Fechar"><X size={19} /></button><span className="modal-icon"><CircleHelp size={26} /></span><h2>Direto ao que importa.</h2><p>Analise seu PC, escolha os ajustes e mande executar. O Zeus confirma as alterações relendo o estado do Windows.</p><p>A limpeza remove temporários com mais de 7 dias. A inicialização controla as entradas do seu usuário; os demais programas ficam nas configurações do Windows.</p><p>Você pode restaurar energia, animações e inicialização pelo histórico. Arquivos removidos pela limpeza não são recuperáveis pelo Zeus.</p><button className="primary" onClick={() => setHelp(false)}>Entendi<Check size={16} /></button></dialog>
  </div>
}
