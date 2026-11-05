import 'package:flutter/material.dart';

import '../../state/app_state.dart';

/// Franja superior que siempre dice si hay conexión y cuántos reportes faltan por enviar.
class ConnectionBanner extends StatelessWidget {
  const ConnectionBanner({super.key, required this.state});

  final AppState state;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final pending = state.counts.pending;
    final pendingText = pending == 0
        ? 'todo está sincronizado'
        : pending == 1
            ? '1 reporte pendiente'
            : '$pending reportes pendientes';

    final (IconData icon, Color background, Color foreground, String text) = switch (state.connection) {
      _ when state.syncing => (Icons.sync, scheme.primaryContainer, scheme.onPrimaryContainer, 'Enviando reportes al sistema central…'),
      LinkStatus.offline => (Icons.cloud_off, const Color(0xFFFFE0B2), Colors.black87, 'Sin conexión: los reportes se guardan en el teléfono ($pendingText).'),
      LinkStatus.serverUnavailable => (Icons.dns_outlined, const Color(0xFFFFE0B2), Colors.black87, 'Hay red, pero el sistema central no responde ($pendingText).'),
      LinkStatus.online => (Icons.cloud_done, scheme.secondaryContainer, scheme.onSecondaryContainer, 'Conectado: $pendingText.'),
      LinkStatus.unknown => (Icons.cloud_queue, scheme.surfaceContainerHighest, scheme.onSurface, 'Comprobando la conexión…'),
    };

    return Material(
      color: background,
      child: SafeArea(
        bottom: false,
        top: false,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
          child: Row(
            children: [
              state.syncing
                  ? SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2.5, color: foreground))
                  : Icon(icon, color: foreground, size: 22),
              const SizedBox(width: 12),
              Expanded(child: Text(text, style: TextStyle(color: foreground))),
              if (!state.syncing && pending > 0 && state.connection != LinkStatus.offline)
                TextButton(
                  onPressed: () => _syncNow(context),
                  style: TextButton.styleFrom(foregroundColor: foreground),
                  child: const Text('Enviar'),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _syncNow(BuildContext context) async {
    final messenger = ScaffoldMessenger.of(context);
    final outcome = await state.syncNow(force: true);
    messenger.showSnackBar(SnackBar(content: Text(outcome.summary)));
  }
}
