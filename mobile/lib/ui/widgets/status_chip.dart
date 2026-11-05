import 'package:flutter/material.dart';

import '../../data/field_report.dart';

/// Etiqueta del estado de sincronización de un reporte.
class StatusChip extends StatelessWidget {
  const StatusChip({super.key, required this.status});

  final ReportStatus status;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (IconData icon, Color color) = switch (status) {
      ReportStatus.pending => (Icons.schedule, const Color(0xFFB7791F)),
      ReportStatus.synced => (Icons.check_circle, const Color(0xFF2F855A)),
      ReportStatus.conflict => (Icons.call_split, scheme.error),
      ReportStatus.rejected => (Icons.error_outline, scheme.error),
    };
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 16, color: color),
        const SizedBox(width: 4),
        Text(status.label, style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 12)),
      ],
    );
  }
}
