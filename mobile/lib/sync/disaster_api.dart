import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import '../data/disaster_event.dart';
import '../data/field_report.dart';

/// Error al hablar con el sistema central.
/// [isConnectivity] es true si no hubo respuesta (sin red, servidor apagado o tiempo agotado).
class ApiException implements Exception {
  const ApiException(this.message, {this.statusCode, this.isConnectivity = false});

  final String message;
  final int? statusCode;
  final bool isConnectivity;

  @override
  String toString() => message;
}

/// Resultado de un registro devuelto por POST /api/v1/sync/batch.
class SyncItemResult {
  const SyncItemResult({required this.clientId, required this.status, this.reason});

  final String clientId;

  /// INSERTED, DUPLICATE, CONFLICT o REJECTED.
  final String status;
  final String? reason;

  factory SyncItemResult.fromJson(Map<String, dynamic> json) {
    final errors = json['errors'];
    final details = errors is Map
        ? errors.values.expand((v) => v is List ? v.map((e) => e.toString()) : [v.toString()]).join(' ')
        : null;
    final reason = [json['reason'] as String?, details].where((s) => s != null && s.isNotEmpty).join(' ');
    return SyncItemResult(
      clientId: json['clientId'] as String,
      status: json['status'] as String,
      reason: reason.isEmpty ? null : reason,
    );
  }
}

class SyncResponse {
  const SyncResponse({required this.results, this.serverTime});

  final List<SyncItemResult> results;
  final DateTime? serverTime;
}

/// Cliente REST del tier de Negocio (contracts/rest/api-v1.md).
class DisasterApi {
  DisasterApi(this.baseUrl, {http.Client? client, this.timeout = const Duration(seconds: 30)})
      : _client = client ?? http.Client();

  final String baseUrl;
  final Duration timeout;
  final http.Client _client;

  /// true si Business responde y su tier de Datos está disponible.
  Future<bool> isAvailable() async {
    try {
      final response = await _client.get(Uri.parse('$baseUrl/health')).timeout(const Duration(seconds: 6));
      return response.statusCode == 200;
    } catch (_) {
      return false;
    }
  }

  Future<List<DisasterEvent>> fetchEvents() async {
    final response = await _send(() => _client.get(Uri.parse('$baseUrl/api/v1/events')));
    final body = jsonDecode(utf8.decode(response.bodyBytes)) as List<dynamic>;
    return body.map((e) => DisasterEvent.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<SyncResponse> syncBatch({
    required String batchId,
    required String deviceId,
    required List<FieldReport> reports,
  }) async {
    final payload = jsonEncode({
      'batchId': batchId,
      'deviceId': deviceId,
      'items': reports.map((r) => r.toSyncJson()).toList(),
    });
    final response = await _send(() => _client.post(
          Uri.parse('$baseUrl/api/v1/sync/batch'),
          headers: {'Content-Type': 'application/json; charset=utf-8'},
          body: payload,
        ));
    final body = jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    return SyncResponse(
      results: (body['results'] as List<dynamic>)
          .map((e) => SyncItemResult.fromJson(e as Map<String, dynamic>))
          .toList(),
      serverTime: body['serverTime'] is String ? DateTime.tryParse(body['serverTime'] as String) : null,
    );
  }

  Future<http.Response> _send(Future<http.Response> Function() request) async {
    final http.Response response;
    try {
      response = await request().timeout(timeout);
    } on TimeoutException {
      throw const ApiException('El servidor tardó demasiado en responder.', isConnectivity: true);
    } on SocketException {
      throw const ApiException('Sin conexión con el servidor.', isConnectivity: true);
    } on http.ClientException {
      throw const ApiException('Sin conexión con el servidor.', isConnectivity: true);
    }
    if (response.statusCode >= 200 && response.statusCode < 300) return response;
    throw ApiException(_problemMessage(response), statusCode: response.statusCode);
  }

  /// Extrae el mensaje de un ProblemDetails (RFC 7807) en español.
  static String _problemMessage(http.Response response) {
    if (response.statusCode == 503) return 'El sistema central no está disponible en este momento.';
    try {
      final body = jsonDecode(utf8.decode(response.bodyBytes));
      if (body is Map<String, dynamic>) {
        final errors = body['errors'];
        if (errors is Map && errors.isNotEmpty) {
          return errors.values.expand((v) => v is List ? v : [v]).join(' ');
        }
        final text = body['detail'] ?? body['title'];
        if (text is String && text.isNotEmpty) return text;
      }
    } catch (_) {
      // Cuerpo que no es JSON: se usa el mensaje genérico.
    }
    return 'El servidor respondió con error ${response.statusCode}.';
  }

  void close() => _client.close();
}
