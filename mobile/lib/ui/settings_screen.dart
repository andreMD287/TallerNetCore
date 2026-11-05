import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../data/settings_store.dart';
import '../state/app_state.dart';

/// Servidor, descarga de eventos y opciones de sincronización.
class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key, required this.state});

  final AppState state;

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  late final TextEditingController _url = TextEditingController(text: widget.state.settings.serverUrl);
  bool _testing = false;
  bool _downloading = false;
  static final _date = DateFormat('d MMM y, HH:mm', 'es');

  @override
  void dispose() {
    _url.dispose();
    super.dispose();
  }

  Future<void> _saveAndTest() async {
    final messenger = ScaffoldMessenger.of(context);
    FocusScope.of(context).unfocus();
    setState(() => _testing = true);
    await widget.state.updateServerUrl(_url.text);
    _url.text = widget.state.settings.serverUrl;
    if (!mounted) return;
    setState(() => _testing = false);
    final ok = widget.state.connection == LinkStatus.online;
    messenger.showSnackBar(SnackBar(
      content: Text(ok ? 'Conexión correcta con el sistema central.' : 'No hubo respuesta del sistema central en esa dirección.'),
    ));
  }

  Future<void> _download() async {
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _downloading = true);
    final error = await widget.state.downloadEvents();
    if (!mounted) return;
    setState(() => _downloading = false);
    messenger.showSnackBar(SnackBar(content: Text(error ?? '${widget.state.eventList.length} eventos descargados.')));
  }

  @override
  Widget build(BuildContext context) {
    final state = widget.state;
    final theme = Theme.of(context);
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final s = state.settings;
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text('Sistema central', style: theme.textTheme.titleMedium),
            const SizedBox(height: 8),
            TextField(
              controller: _url,
              keyboardType: TextInputType.url,
              decoration: const InputDecoration(
                labelText: 'Dirección del servidor',
                helperText: 'Emulador: http://10.0.2.2:5002 · Teléfono: http://<IP del computador>:5002',
              ),
              onSubmitted: (_) => _saveAndTest(),
            ),
            const SizedBox(height: 8),
            FilledButton.tonalIcon(
              onPressed: _testing ? null : _saveAndTest,
              icon: _testing
                  ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.network_check),
              label: const Text('Guardar y probar conexión'),
            ),
            const Divider(height: 32),
            Text('Eventos para trabajar sin conexión', style: theme.textTheme.titleMedium),
            const SizedBox(height: 4),
            Text(state.eventsDownloadedAt == null
                ? 'Aún no se han descargado eventos.'
                : '${state.eventList.length} eventos · descargados el ${_date.format(state.eventsDownloadedAt!.toLocal())}'),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: _downloading ? null : _download,
              icon: const Icon(Icons.download),
              label: Text(_downloading ? 'Descargando…' : 'Descargar eventos'),
            ),
            const Divider(height: 32),
            Text('Sincronización', style: theme.textTheme.titleMedium),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Enviar automáticamente'),
              subtitle: const Text('Al recuperar la conexión, al abrir la app y cada minuto mientras está abierta.'),
              value: s.autoSync,
              onChanged: state.updateAutoSync,
            ),
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Reportes por envío'),
              subtitle: const Text('Lotes más pequeños funcionan mejor con señal débil.'),
              trailing: DropdownButton<int>(
                value: SettingsStore.batchSizes.contains(s.batchSize) ? s.batchSize : SettingsStore.defaultBatchSize,
                items: [for (final n in SettingsStore.batchSizes) DropdownMenuItem(value: n, child: Text('$n'))],
                onChanged: (v) => v == null ? null : state.updateBatchSize(v),
              ),
            ),
            if (s.lastSyncAt != null)
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text('Último envío: ${_date.format(s.lastSyncAt!.toLocal())}'),
                subtitle: Text(s.lastSyncSummary ?? ''),
              ),
            FilledButton.icon(
              onPressed: state.syncing || state.counts.pending == 0
                  ? null
                  : () async {
                      final messenger = ScaffoldMessenger.of(context);
                      final outcome = await state.syncNow(force: true);
                      messenger.showSnackBar(SnackBar(content: Text(outcome.summary)));
                    },
              icon: const Icon(Icons.sync),
              label: Text('Sincronizar ahora (${state.counts.pending} pendientes)'),
            ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: state.counts.synced == 0
                  ? null
                  : () async {
                      final messenger = ScaffoldMessenger.of(context);
                      final removed = await state.clearSynced();
                      messenger.showSnackBar(SnackBar(content: Text('$removed reportes enviados se quitaron del teléfono.')));
                    },
              icon: const Icon(Icons.cleaning_services_outlined),
              label: Text('Quitar del teléfono los ya enviados (${state.counts.synced})'),
            ),
            const Divider(height: 32),
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Identificador de este teléfono'),
              subtitle: SelectableText(s.deviceId),
            ),
          ],
        );
      },
    );
  }
}
