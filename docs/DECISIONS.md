# Registro de decisiones

Cada rol registra aquí las decisiones no escritas en el plan base, con prefijo `R1-` (Rol 1) o `R2-` (Rol 2).
Formato: contexto, alternativas, decisión, consecuencias.

## R1-01 · Fuente de paquetes NuGet fijada en el repo
- **Contexto:** con una instalación nueva del SDK no había fuentes NuGet configuradas y la restauración fallaba.
- **Alternativas:** (a) que cada integrante configure su máquina; (b) `nuget.config` en la raíz del repo con `nuget.org`.
- **Decisión:** (b).
- **Consecuencias:** restauración reproducible en cualquier máquina y en Docker; un archivo más en la raíz.
