import 'package:flutter/material.dart';

import '../state/app_state.dart';
import 'capture_screen.dart';
import 'map_screen.dart';
import 'reports_screen.dart';
import 'settings_screen.dart';
import 'widgets/connection_banner.dart';

/// Estructura principal: barra de estado de conexión arriba y cuatro pestañas abajo.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.state});

  final AppState state;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _tab = 0;

  static const _titles = ['Nuevo reporte', 'Mis reportes', 'Mapa', 'Ajustes'];

  @override
  Widget build(BuildContext context) {
    final state = widget.state;
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final attention = state.counts.needsAttention;
        return Scaffold(
          appBar: AppBar(title: Text(_titles[_tab])),
          body: Column(
            children: [
              ConnectionBanner(state: state),
              Expanded(
                child: IndexedStack(
                  index: _tab,
                  children: [
                    CaptureScreen(state: state),
                    ReportsScreen(state: state),
                    MapScreen(state: state, active: _tab == 2),
                    SettingsScreen(state: state),
                  ],
                ),
              ),
            ],
          ),
          bottomNavigationBar: NavigationBar(
            selectedIndex: _tab,
            onDestinationSelected: (i) => setState(() => _tab = i),
            destinations: [
              const NavigationDestination(icon: Icon(Icons.add_location_alt_outlined), selectedIcon: Icon(Icons.add_location_alt), label: 'Reportar'),
              NavigationDestination(
                icon: Badge(
                  isLabelVisible: state.counts.pending + attention > 0,
                  label: Text('${state.counts.pending + attention}'),
                  backgroundColor: attention > 0 ? Theme.of(context).colorScheme.error : null,
                  child: const Icon(Icons.list_alt_outlined),
                ),
                selectedIcon: const Icon(Icons.list_alt),
                label: 'Reportes',
              ),
              const NavigationDestination(icon: Icon(Icons.map_outlined), selectedIcon: Icon(Icons.map), label: 'Mapa'),
              const NavigationDestination(icon: Icon(Icons.settings_outlined), selectedIcon: Icon(Icons.settings), label: 'Ajustes'),
            ],
          ),
        );
      },
    );
  }
}
