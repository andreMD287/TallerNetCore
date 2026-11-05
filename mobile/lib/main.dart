import 'package:flutter/material.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'data/local_database.dart';
import 'data/report_repository.dart';
import 'data/settings_store.dart';
import 'state/app_state.dart';
import 'ui/home_shell.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('es');
  final db = await LocalDatabase.open();
  final state = AppState(
    reports: ReportRepository(db),
    events: EventRepository(db),
    settings: await SettingsStore.load(),
  );
  runApp(CampoDesastresApp(state: state));
  await state.start();
}

class CampoDesastresApp extends StatelessWidget {
  const CampoDesastresApp({super.key, required this.state});

  final AppState state;

  @override
  Widget build(BuildContext context) {
    const seed = Color(0xFF1F5F8B);
    return MaterialApp(
      title: 'DisasterMaps Campo',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: seed),
        inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
      ),
      darkTheme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: seed, brightness: Brightness.dark),
        inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
      ),
      home: HomeShell(state: state),
    );
  }
}
