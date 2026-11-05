import 'package:flutter/material.dart';

import '../../core/affectation_level.dart';

/// Selector del nivel de afectación 1..5 con botones grandes de colores, cómodo de usar en campo.
class LevelSelector extends StatelessWidget {
  const LevelSelector({super.key, required this.value, required this.onChanged, this.errorText});

  final int? value;
  final ValueChanged<int> onChanged;
  final String? errorText;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            for (final level in AffectationLevel.values)
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 3),
                  child: _LevelButton(level: level, selected: value == level, onTap: () => onChanged(level)),
                ),
              ),
          ],
        ),
        const SizedBox(height: 8),
        if (value != null)
          Text(
            'Nivel $value · ${AffectationLevel.label(value!)}: ${AffectationLevel.description(value!)}',
            style: theme.textTheme.bodyMedium,
          )
        else
          Text(
            errorText ?? 'Elija de 1 (mínima) a 5 (crítica).',
            style: theme.textTheme.bodyMedium?.copyWith(color: errorText != null ? theme.colorScheme.error : null),
          ),
      ],
    );
  }
}

class _LevelButton extends StatelessWidget {
  const _LevelButton({required this.level, required this.selected, required this.onTap});

  final int level;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final color = AffectationLevel.color(level);
    return Semantics(
      button: true,
      selected: selected,
      label: 'Nivel $level, ${AffectationLevel.label(level)}',
      child: Material(
        color: selected ? color : color.withValues(alpha: 0.18),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
          side: BorderSide(color: color, width: selected ? 3 : 1.5),
        ),
        child: InkWell(
          borderRadius: BorderRadius.circular(12),
          onTap: onTap,
          child: SizedBox(
            height: 64,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  '$level',
                  style: TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w700,
                    color: selected ? AffectationLevel.onColor(level) : Theme.of(context).colorScheme.onSurface,
                  ),
                ),
                Text(
                  AffectationLevel.label(level),
                  style: TextStyle(
                    fontSize: 11,
                    color: selected ? AffectationLevel.onColor(level) : Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Círculo con el número del nivel, para listas y marcadores.
class LevelBadge extends StatelessWidget {
  const LevelBadge({super.key, required this.level, this.size = 40});

  final int level;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: AffectationLevel.color(level),
          shape: BoxShape.circle,
          border: Border.all(color: Colors.white, width: 2),
          boxShadow: const [BoxShadow(blurRadius: 3, color: Colors.black26)],
        ),
        child: Text(
          '$level',
          style: TextStyle(color: AffectationLevel.onColor(level), fontWeight: FontWeight.w700, fontSize: size * 0.42),
        ),
      );
}
