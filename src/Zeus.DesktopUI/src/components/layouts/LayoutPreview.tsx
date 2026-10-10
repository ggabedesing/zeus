import { useEffect, useState } from 'react'
import type { DesktopLayout } from '@/layouts/types'
export function LayoutPreview({ layout, imageUrl }: { layout: DesktopLayout; imageUrl?: string }) {
  const [now, setNow] = useState(() => new Date())
  const [imageFailed, setImageFailed] = useState(false)
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 1000); return () => clearInterval(timer) }, [])
  useEffect(() => { setImageFailed(false) }, [imageUrl])
  const isNeon = layout.preview.style === 'neon'
  return <div className="wallpaper-preview">
    <div className="wallpaper-screen" style={{ background: `radial-gradient(ellipse at 72% 30%, ${layout.colors.accent}${isNeon ? '66' : '44'}, transparent 60%), radial-gradient(ellipse at 15% 85%, ${layout.colors.primary}33, transparent 55%), ${layout.colors.background}` }}>
      {imageUrl && !imageFailed ? <img className="wallpaper-image" src={imageUrl} alt={`Papel de parede gerado: ${layout.name}`} onError={() => setImageFailed(true)} /> : <div className="preview-clock"><span>{now.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span><small>{now.toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' })}</small><p>Relógio ilustrativo · não será instalado</p></div>}
    </div>
    <div className="preview-label">{imageUrl && !imageFailed ? 'Papel de parede que será aplicado' : 'Amostra ilustrativa do estilo · confira a imagem na próxima etapa'}</div>
  </div>
}
