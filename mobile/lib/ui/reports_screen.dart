import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../core/coordinates.dart';
import '../data/field_report.dart';
import '../state/app_state.dart';
import 'report_detail_screen.dart';
import 'widgets/level_selector.dart';
import 'widgets/status_chip.dart';

enum _Filter { all, pending, synced, attention }

/// Lista de reportes guardados en el teléfono, con su estado de sincronización.
class ReportsScreen extends StatefulWidget {
  const ReportsScreen({super.key, required this.state});

  final AppState state;

  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> {
  _Filter _filter = _Filter.all;
  static final _date = DateFormat('d MMM y, HH:mm', 'es');

  Set<ReportStatus>? get _statuses => switch (_filter) {
        _Filter.all => null,
        _Filter.pending => {ReportStatus.pending},
        _Filter.synced => {ReportStatus.synced},
        _Filter.attention => {ReportStatus.conflict, ReportStatus.rejected},
      };

  @override
  Widget build(BuildContext context) {
    final state = widget.state;
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final c = state.counts;
        return Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 4),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: SegmentedButton<_Filter>(
                  segments: [
                    ButtonSegment(value: _Filter.all, label: Text('Todos (${c.total})')),
                    ButtonSegment(value: _Filter.pending, label: Text('Pendientes (${c.pending})')),
                    ButtonSegment(value: _Filter.synced, label: Text('Enviados (${c.synced})')),
                    ButtonSegment(value: _Filter.attention, label: Text('Revisar (${c.needsAttention})')),
                  ],
                  selected: {_filter},
                  showSelectedIcon: false,
                  onSelectionChanged: (s) => setState(() => _filter = s.first),
                ),
              ),
            ),
            Expanded(
              child: FutureBuilder<List<FieldReport>>(
                // La revisión cambia cada vez que se guardan o sincronizan reportes.
                key: ValueKey('${state.revision}-$_filter'),
                future: state.reports.all(statuses: _statuses),
                builder: (context, snapshot) {
                  if (!snapshot.hasData) return const Center(child: CircularProgressIndicator());
                  final items = snapshot.data!;
                  return RefreshIndicator(
                    onRefresh: () async {
                      final messenger = ScaffoldMessenger.of(context);
                      final outcome = await state.syncNow(force: true);
                      messenger.showSnackBar(SnackBar(content: Text(outcome.summary)));
                    },
                    child: items.isEmpty
                        ? ListView(children: const [
                            SizedBox(height: 120),
                            Center(child: Text('No hay reportes en esta vista.')),
                          ])
                        : ListView.separated(
                            itemCount: items.length,
                            separatorBuilder: (_, _) => const Divider(height: 1),
                            itemBuilder: (context, i) => _tile(context, items[i]),
                          ),
                  );
                },
              ),
            ),
          ],
        );
      },
    );
  }

  Widget _tile(BuildContext context, FieldReport r) => ListTile(
        leading: LevelBadge(level: r.affectationLevel),
        title: Text(Coordinates.formatPair(r.latitude, r.longitude)),
        subtitle: Text(
          [
            r.eventName,
            _date.format(r.capturedAt.toLocal()),
            if (r.status.needsAttention && r.serverReason != null) r.serverReason!,
            if (r.status == ReportStatus.pending && r.lastError != null) 'Último intento: ${r.lastError}',
          ].join('\n'),
          maxLines: 4,
          overflow: TextOverflow.ellipsis,
        ),
        isThreeLine: true,
        trailing: StatusChip(status: r.status),
        onTap: () => Navigator.of(context).push(MaterialPageRoute(
          builder: (_) => ReportDetailScreen(state: widget.state, clientId: r.clientId),
        )),
      );
}
