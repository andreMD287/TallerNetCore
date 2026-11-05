import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';
import 'package:uuid/uuid.dart';

import '../core/coordinates.dart';
import '../data/disaster_event.dart';
import '../data/field_report.dart';
import '../state/app_state.dart';
import 'map_picker_screen.dart';
import 'widgets/level_selector.dart';

/// Formulario de captura. Con [initial] corrige un reporte existente (conserva su id).
class CaptureScreen extends StatefulWidget {
  const CaptureScreen({super.key, required this.state, this.initial});

  final AppState state;
  final FieldReport? initial;

  @override
  State<CaptureScreen> createState() => _CaptureScreenState();
}

class _CaptureScreenState extends State<CaptureScreen> {
  final _form = GlobalKey<FormState>();
  final _latitude = TextEditingController();
  final _longitude = TextEditingController();
  final _people = TextEditingController();
  final _municipality = TextEditingController();
  final _department = TextEditingController();
  final _description = TextEditingController();

  String? _eventId;
  int? _level;
  bool _levelMissing = false;
  LocationSource _source = LocationSource.manual;
  double? _accuracy;
  DateTime? _capturedAt;
  bool _locating = false;
  bool _saving = false;

  bool get _editing => widget.initial != null;

  @override
  void initState() {
    super.initState();
    final r = widget.initial;
    if (r != null) {
      _eventId = r.eventId;
      _level = r.affectationLevel;
      _source = r.locationSource;
      _accuracy = r.accuracyMeters;
      _capturedAt = r.capturedAt;
      _latitude.text = Coordinates.format(r.latitude);
      _longitude.text = Coordinates.format(r.longitude);
      _people.text = r.affectedPeople == 0 ? '' : '${r.affectedPeople}';
      _municipality.text = r.municipality;
      _department.text = r.department;
      _description.text = r.description;
    }
  }

  @override
  void dispose() {
    for (final c in [_latitude, _longitude, _people, _municipality, _department, _description]) {
      c.dispose();
    }
    super.dispose();
  }

  // ---------- Ubicación ----------

  Future<void> _useGps() async {
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _locating = true);
    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        messenger.showSnackBar(const SnackBar(content: Text('Active la ubicación (GPS) del teléfono.')));
        return;
      }
      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
      if (permission == LocationPermission.denied || permission == LocationPermission.deniedForever) {
        messenger.showSnackBar(const SnackBar(
            content: Text('Sin permiso de ubicación. Puede elegir el punto en el mapa o escribir las coordenadas.')));
        return;
      }
      Position? position;
      try {
        // El GPS funciona sin internet: usa los satélites, no la red.
        position = await Geolocator.getCurrentPosition(
          locationSettings: const LocationSettings(accuracy: LocationAccuracy.best, timeLimit: Duration(seconds: 30)),
        );
      } catch (_) {
        position = await Geolocator.getLastKnownPosition();
        if (position != null) {
          messenger.showSnackBar(const SnackBar(content: Text('No hubo señal a tiempo; se usó la última ubicación conocida.')));
        }
      }
      if (position == null) {
        messenger.showSnackBar(const SnackBar(content: Text('No se pudo obtener la ubicación. Intente al aire libre.')));
        return;
      }
      _setLocation(position.latitude, position.longitude, LocationSource.gps, position.accuracy);
    } finally {
      if (mounted) setState(() => _locating = false);
    }
  }

  Future<void> _pickOnMap() async {
    final lat = Coordinates.parse(_latitude.text);
    final lon = Coordinates.parse(_longitude.text);
    final picked = await Navigator.of(context).push<LatLng>(MaterialPageRoute(
      builder: (_) => MapPickerScreen(initial: lat != null && lon != null ? LatLng(lat, lon) : null),
    ));
    if (picked != null) _setLocation(picked.latitude, picked.longitude, LocationSource.map, null);
  }

  void _setLocation(double lat, double lon, LocationSource source, double? accuracy) {
    setState(() {
      _latitude.text = Coordinates.format(lat);
      _longitude.text = Coordinates.format(lon);
      _source = source;
      _accuracy = accuracy;
    });
  }

  void _onCoordinateTyped(String _) {
    if (_source != LocationSource.manual) {
      setState(() {
        _source = LocationSource.manual;
        _accuracy = null;
      });
    }
  }

  // ---------- Guardar ----------

  Future<void> _save(List<DisasterEvent> events) async {
    final valid = _form.currentState!.validate();
    setState(() => _levelMissing = _level == null);
    if (!valid || _level == null) return;

    final lat = Coordinates.parse(_latitude.text)!;
    final lon = Coordinates.parse(_longitude.text)!;
    final pairError = Coordinates.validatePair(lat, lon);
    if (pairError != null) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(pairError)));
      return;
    }
    final event = events.firstWhere((e) => e.id == _eventId,
        orElse: () => DisasterEvent(id: _eventId!, name: widget.initial?.eventName ?? 'Evento', type: 'OTHER'));

    setState(() => _saving = true);
    final report = FieldReport(
      clientId: widget.initial?.clientId ?? const Uuid().v7(),
      eventId: event.id,
      eventName: event.name,
      latitude: double.parse(Coordinates.format(lat)),
      longitude: double.parse(Coordinates.format(lon)),
      accuracyMeters: _accuracy,
      locationSource: _source,
      affectationLevel: _level!,
      affectedPeople: int.tryParse(_people.text.trim()) ?? 0,
      municipality: _municipality.text.trim(),
      department: _department.text.trim(),
      description: _description.text.trim(),
      capturedAt: _capturedAt ?? DateTime.now().toUtc(),
    );
    await widget.state.saveReport(report);
    if (!mounted) return;
    setState(() => _saving = false);

    final online = widget.state.connection == LinkStatus.online;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(online
          ? 'Reporte guardado. Se está enviando al sistema central.'
          : 'Reporte guardado en el teléfono. Se enviará cuando haya conexión.'),
    ));
    if (_editing) {
      Navigator.of(context).pop();
    } else {
      _clearForNext();
    }
  }

  /// Deja el evento, el municipio y el departamento para agilizar el siguiente reporte del mismo sitio.
  void _clearForNext() {
    _form.currentState!.reset();
    setState(() {
      _latitude.clear();
      _longitude.clear();
      _people.clear();
      _description.clear();
      _level = null;
      _levelMissing = false;
      _source = LocationSource.manual;
      _accuracy = null;
      _capturedAt = null;
    });
  }

  // ---------- Interfaz ----------

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final events = widget.state.eventList;
        final body = events.isEmpty && !_editing ? _noEvents(context) : _formBody(context, events);
        if (!_editing) return body;
        return Scaffold(appBar: AppBar(title: const Text('Corregir reporte')), body: body);
      },
    );
  }

  Widget _noEvents(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.download_for_offline_outlined, size: 56, color: Theme.of(context).colorScheme.primary),
              const SizedBox(height: 16),
              const Text(
                'Antes de salir a campo, descargue los eventos activos mientras tenga conexión. '
                'Después podrá reportar sin internet.',
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton.icon(
                onPressed: () async {
                  final messenger = ScaffoldMessenger.of(context);
                  final error = await widget.state.downloadEvents();
                  messenger.showSnackBar(SnackBar(content: Text(error ?? 'Eventos descargados.')));
                },
                icon: const Icon(Icons.download),
                label: const Text('Descargar eventos'),
              ),
            ],
          ),
        ),
      );

  Widget _formBody(BuildContext context, List<DisasterEvent> events) {
    final theme = Theme.of(context);
    final eventIds = events.map((e) => e.id).toSet();
    final selectedEvent = eventIds.contains(_eventId) ? _eventId : null;

    return Form(
      key: _form,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
        children: [
          DropdownButtonFormField<String>(
            initialValue: selectedEvent,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'Evento de desastre', prefixIcon: Icon(Icons.warning_amber)),
            items: [
              for (final e in events)
                DropdownMenuItem(value: e.id, child: Text('${e.name} · ${e.typeLabel}', overflow: TextOverflow.ellipsis)),
            ],
            onChanged: (v) => setState(() => _eventId = v),
            validator: (v) => v == null && _eventId == null ? 'Elija el evento.' : null,
          ),
          const SizedBox(height: 20),
          Text('Ubicación del sitio', style: theme.textTheme.titleMedium),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: FilledButton.tonalIcon(
                  onPressed: _locating ? null : _useGps,
                  icon: _locating
                      ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2))
                      : const Icon(Icons.my_location),
                  label: Text(_locating ? 'Buscando…' : 'Usar GPS'),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: _pickOnMap,
                  icon: const Icon(Icons.place_outlined),
                  label: const Text('Elegir en mapa'),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(child: _coordinateField(_latitude, 'Latitud', Coordinates.validateLatitude)),
              const SizedBox(width: 8),
              Expanded(child: _coordinateField(_longitude, 'Longitud', Coordinates.validateLongitude)),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            _accuracy != null
                ? '${_source.label} · precisión ±${_accuracy!.toStringAsFixed(0)} m'
                : '${_source.label} · grados decimales (WGS84)',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: 20),
          Text('Nivel de afectación', style: theme.textTheme.titleMedium),
          const SizedBox(height: 8),
          LevelSelector(
            value: _level,
            errorText: _levelMissing ? 'Elija el nivel de afectación.' : null,
            onChanged: (v) => setState(() {
              _level = v;
              _levelMissing = false;
            }),
          ),
          const SizedBox(height: 20),
          Text('Detalles (opcional)', style: theme.textTheme.titleMedium),
          const SizedBox(height: 8),
          TextFormField(
            controller: _people,
            keyboardType: TextInputType.number,
            inputFormatters: [FilteringTextInputFormatter.digitsOnly],
            decoration: const InputDecoration(labelText: 'Personas afectadas', prefixIcon: Icon(Icons.groups_outlined)),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(child: TextFormField(controller: _municipality, decoration: const InputDecoration(labelText: 'Municipio'))),
              const SizedBox(width: 8),
              Expanded(child: TextFormField(controller: _department, decoration: const InputDecoration(labelText: 'Departamento'))),
            ],
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _description,
            maxLines: 3,
            maxLength: 500,
            decoration: const InputDecoration(labelText: 'Descripción', alignLabelWithHint: true),
          ),
          const SizedBox(height: 8),
          FilledButton.icon(
            onPressed: _saving ? null : () => _save(events),
            style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
            icon: const Icon(Icons.save_alt),
            label: Text(_editing ? 'Guardar corrección' : 'Guardar reporte'),
          ),
          const SizedBox(height: 8),
          Text(
            'Se guarda primero en el teléfono y se envía al sistema central en cuanto haya conexión.',
            style: theme.textTheme.bodySmall,
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }

  Widget _coordinateField(TextEditingController controller, String label, String? Function(String?) validator) =>
      TextFormField(
        controller: controller,
        keyboardType: const TextInputType.numberWithOptions(decimal: true, signed: true),
        inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[-0-9.,]'))],
        decoration: InputDecoration(labelText: label),
        validator: validator,
        onChanged: _onCoordinateTyped,
      );
}
