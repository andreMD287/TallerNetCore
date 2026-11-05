import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';

import '../core/affectation_level.dart';
import '../core/coordinates.dart';
import '../data/field_report.dart';
import '../state/app_state.dart';
import 'capture_screen.dart';
import 'widgets/level_selector.dart';
import 'widgets/status_chip.dart';

/// Detalle de un reporte y acciones según su estado.
class ReportDetailScreen extends StatelessWidget {
  const ReportDetailScreen({super.key, required this.state, required this.clientId});

  final AppState state;
  final String clientId;

  static final _date = DateFormat("d 'de' MMMM 'de' y, HH:mm:ss", 'es');

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) => FutureBuilder<FieldReport?>(
        key: ValueKey(state.revision),
        future: state.reports.find(clientId),
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Scaffold(body: Center(child: CircularProgressIndicator()));
          }
          final r = snapshot.data;
          if (r == null) {
            return Scaffold(appBar: AppBar(), body: const Center(child: Text('El reporte ya no está en el teléfono.')));
          }
          return Scaffold(
            appBar: AppBar(title: const Text('Detalle del reporte')),
            body: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Row(
                  children: [
                    LevelBadge(level: r.affectationLevel, size: 56),
                    const SizedBox(width: 16),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Nivel ${r.affectationLevel} · ${AffectationLevel.label(r.affectationLevel)}',
                              style: Theme.of(context).textTheme.titleLarge),
                          const SizedBox(height: 4),
                          StatusChip(status: r.status),
                        ],
                      ),
                    ),
                  ],
                ),
                if (r.status.needsAttention && r.serverReason != null) ...[
                  const SizedBox(height: 16),
                  Card(
                    color: Theme.of(context).colorScheme.errorContainer,
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Text(r.serverReason!, style: TextStyle(color: Theme.of(context).colorScheme.onErrorContainer)),
                    ),
                  ),
                ],
                const SizedBox(height: 16),
                _row(context, 'Latitud', Coordinates.format(r.latitude), copy: true),
                _row(context, 'Longitud', Coordinates.format(r.longitude), copy: true),
                _row(context, 'Origen de la ubicación',
                    r.accuracyMeters != null ? '${r.locationSource.label} (±${r.accuracyMeters!.toStringAsFixed(0)} m)' : r.locationSource.label),
                _row(context, 'Evento', r.eventName),
                _row(context, 'Personas afectadas', '${r.affectedPeople}'),
                if (r.municipality.isNotEmpty || r.department.isNotEmpty)
                  _row(context, 'Lugar', [r.municipality, r.department].where((s) => s.isNotEmpty).join(', ')),
                if (r.description.isNotEmpty) _row(context, 'Descripción', r.description),
                _row(context, 'Capturado', _date.format(r.capturedAt.toLocal())),
                if (r.syncedAt != null) _row(context, 'Confirmado por el sistema central', _date.format(r.syncedAt!.toLocal())),
                if (r.status == ReportStatus.pending) ...[
                  _row(context, 'Intentos de envío', '${r.attempts}'),
                  if (r.lastError != null) _row(context, 'Último error', r.lastError!),
                  if (r.nextAttemptAt != null) _row(context, 'Próximo intento automático', _date.format(r.nextAttemptAt!.toLocal())),
                ],
                _row(context, 'Identificador', r.clientId, copy: true),
                const SizedBox(height: 24),
                ..._actions(context, r),
              ],
            ),
          );
        },
      ),
    );
  }

  List<Widget> _actions(BuildContext context, FieldReport r) {
    final edit = FilledButton.tonalIcon(
      onPressed: () => Navigator.of(context).push(MaterialPageRoute(
        builder: (_) => CaptureScreen(state: state, initial: r),
      )),
      icon: const Icon(Icons.edit),
      label: Text(r.status == ReportStatus.rejected ? 'Corregir y reenviar' : 'Editar'),
    );
    final delete = OutlinedButton.icon(
      onPressed: () => _confirmDelete(context, r),
      icon: const Icon(Icons.delete_outline),
      label: const Text('Eliminar del teléfono'),
    );
    return switch (r.status) {
      ReportStatus.pending || ReportStatus.rejected => [edit, const SizedBox(height: 8), delete],
      ReportStatus.conflict => [
          const Text(
            'El sistema central ya tiene un reporte con este identificador y otros datos. '
            'El original no se modifica. Puede enviar este como un reporte nuevo o descartarlo.',
          ),
          const SizedBox(height: 12),
          FilledButton.tonalIcon(
            onPressed: () async {
              await state.resendAsNew(r);
              if (context.mounted) Navigator.of(context).pop();
            },
            icon: const Icon(Icons.call_split),
            label: const Text('Enviar como reporte nuevo'),
          ),
          const SizedBox(height: 8),
          delete,
        ],
      ReportStatus.synced => [
          const Text('Este reporte ya está en el sistema central y no se puede modificar desde el teléfono.'),
        ],
    };
  }

  Future<void> _confirmDelete(BuildContext context, FieldReport r) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('¿Eliminar el reporte?'),
        content: const Text('Todavía no está en el sistema central. Si lo elimina, se pierde.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Eliminar')),
        ],
      ),
    );
    if (ok == true) {
      await state.deleteReport(r.clientId);
      if (context.mounted) Navigator.of(context).pop();
    }
  }

  Widget _row(BuildContext context, String label, String value, {bool copy = false}) => ListTile(
        contentPadding: EdgeInsets.zero,
        dense: true,
        title: Text(label, style: Theme.of(context).textTheme.labelMedium),
        subtitle: SelectableText(value, style: Theme.of(context).textTheme.bodyLarge),
        trailing: copy
            ? IconButton(
                tooltip: 'Copiar',
                icon: const Icon(Icons.copy, size: 18),
                onPressed: () {
                  Clipboard.setData(ClipboardData(text: value));
                  ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('$label copiada.')));
                },
              )
            : null,
      );
}
