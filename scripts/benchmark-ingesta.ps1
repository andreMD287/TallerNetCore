<#
.SYNOPSIS
  Compara las dos estrategias de carga por lotes (DAL y DATABASE) contra PostgreSQL real.

.DESCRIPTION
  Para cada tamaño de lote y cada estrategia envía a POST /api/v1/sync/batch:
    1. un lote nuevo (todo se inserta) y
    2. el mismo lote otra vez (todo vuelve como DUPLICATE: mide el costo de la deduplicación).
  Repite cada medición y reporta la mediana del tiempo medido en el tier de Datos (elapsedMs)
  y del tiempo total de la solicitud HTTP. Deja los resultados en un CSV.

.EXAMPLE
  docker compose up -d --build
  ./scripts/benchmark-ingesta.ps1
  ./scripts/benchmark-ingesta.ps1 -Sizes 100,1000 -Repetitions 5 -ChunkSize 250
#>
param(
    [string]$BaseUrl = "http://localhost:5002",
    [int[]]$Sizes = @(100, 1000, 5000, 10000),
    [string[]]$Strategies = @("DAL", "DATABASE"),
    [int]$ChunkSize = 500,
    [int]$Repetitions = 3,
    [string]$Output = "benchmark-ingesta.csv"
)

$ErrorActionPreference = "Stop"
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

function Get-Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return $sorted[[int][math]::Floor($n / 2)] }
    return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2
}

function New-Batch([string]$eventId, [int]$size) {
    $random = New-Object System.Random
    $now = [DateTime]::UtcNow
    $items = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $size; $i++) {
        # Puntos alrededor de Mocoa (1.15, -76.65) en un radio aproximado de 10 km.
        $items.Add([ordered]@{
            clientId         = [guid]::NewGuid().ToString()
            eventId          = $eventId
            latitude         = [math]::Round(1.15 + ($random.NextDouble() - 0.5) * 0.18, 6)
            longitude        = [math]::Round(-76.65 + ($random.NextDouble() - 0.5) * 0.18, 6)
            affectationLevel = $random.Next(1, 6)
            affectedPeople   = $random.Next(0, 300)
            municipality     = "Mocoa"
            department       = "Putumayo"
            description      = "Prueba de carga"
            reportedAt       = $now.AddMinutes(-$random.Next(10, 600)).ToString("o", $invariant)
        })
    }
    $body = [ordered]@{ batchId = [guid]::NewGuid().ToString(); deviceId = "benchmark"; items = $items }
    return ($body | ConvertTo-Json -Depth 5 -Compress)
}

function Send-Batch([string]$json, [string]$strategy) {
    $uri = "$BaseUrl/api/v1/sync/batch?strategy=$strategy&chunkSize=$ChunkSize"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $report = Invoke-RestMethod -Method Post -Uri $uri -Body $bytes -ContentType "application/json; charset=utf-8" -TimeoutSec 300
    $watch.Stop()
    return [pscustomobject]@{ Report = $report; HttpMs = $watch.Elapsed.TotalMilliseconds }
}

$events = Invoke-RestMethod -Uri "$BaseUrl/api/v1/events"
if (-not $events -or $events.Count -eq 0) { throw "No hay eventos. Cree uno antes de medir." }
$eventId = @($events)[0].id
Write-Host "Evento: $(@($events)[0].name) ($eventId). Paquetes de $ChunkSize, $Repetitions repeticiones."

# Calentamiento: la primera llamada paga la compilación JIT y la apertura de conexiones.
foreach ($s in $Strategies) { [void](Send-Batch (New-Batch $eventId 50) $s) }

$rows = @()
foreach ($size in $Sizes) {
    foreach ($strategy in $Strategies) {
        $newData = @(); $newHttp = @(); $dupData = @(); $dupHttp = @()
        for ($r = 1; $r -le $Repetitions; $r++) {
            $json = New-Batch $eventId $size
            $first = Send-Batch $json $strategy
            if ($first.Report.inserted -ne $size) { throw "Se esperaban $size insertados con $strategy y llegaron $($first.Report.inserted)." }
            $again = Send-Batch $json $strategy
            if ($again.Report.duplicates -ne $size) { throw "Se esperaban $size duplicados con $strategy y llegaron $($again.Report.duplicates)." }
            $newData += $first.Report.elapsedMs; $newHttp += $first.HttpMs
            $dupData += $again.Report.elapsedMs; $dupHttp += $again.HttpMs
        }
        foreach ($case in @(
            @{ Name = "nuevos"; Data = $newData; Http = $newHttp },
            @{ Name = "reenvio"; Data = $dupData; Http = $dupHttp })) {
            $dataMs = Get-Median $case.Data
            $rows += [pscustomobject]@{
                Registros        = $size
                Estrategia       = $strategy
                Caso             = $case.Name
                DatosMs          = [math]::Round($dataMs, 0)
                HttpMs           = [math]::Round((Get-Median $case.Http), 0)
                RegistrosPorSeg  = [math]::Round($size / [math]::Max($dataMs, 1) * 1000, 0)
            }
        }
        Write-Host ("{0,6} registros  {1,-8} listo" -f $size, $strategy)
    }
}

$rows | Format-Table -AutoSize
$rows | Export-Csv -Path $Output -NoTypeInformation -Encoding UTF8
Write-Host "Resultados en $Output"
