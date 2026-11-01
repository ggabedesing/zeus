# Zeus — interface

Interface do otimizador local Zeus: Otimizar, Personalizar, Sistema e Histórico. Inclui a seleção de temas, imagem de bloqueio, cenas animadas e layouts próprios do Zeus, preferências por aplicativo e manutenção do Windows com saída real.

O motor está no repositório https://github.com/ggabedesing/zeus-pc/tree/codex/zeus-otimizacao.

```powershell
npm ci
npm run dev
```

O desenvolvimento abre em http://127.0.0.1:5173 e usa o motor em http://127.0.0.1:8000. O proxy aceita somente origens locais previstas. Para produção, `npm run build`; copie o conteúdo de `dist/` para `web/` do motor.

Sem motor local, a interface oferece acesso ao aplicativo de Windows. Ela não simula medições nem afirma executar ajustes remotamente.

O editor de ambiente em Personalizar configura widgets, cores, densidade e posições. A prévia identifica os números ilustrativos; o ambiente nativo lê métricas reais no Windows. Notas e posições são salvas localmente pelo motor.
