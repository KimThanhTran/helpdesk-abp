import 'dart:convert';

import 'package:http/http.dart' as http;

/// Where the API lives. Build with --dart-define=API_URL=https://... ; the Android emulator reaches the host as 10.0.2.2.
const apiUrl = String.fromEnvironment('API_URL', defaultValue: 'http://localhost:8080');

/// What the API sends for enums: numbers. Same values as TicketStatus and TicketPriority on the server.
const statusNames = {1: 'New', 2: 'Open', 3: 'Waiting', 4: 'Resolved', 5: 'Closed', 6: 'Cancelled'};
const priorityNames = {1: 'Low', 2: 'Normal', 3: 'High', 4: 'Urgent'};

class ApiException implements Exception {
  ApiException(this.status, this.message);
  final int status;
  final String message;
  @override
  String toString() => message;
}

class Person {
  Person(this.id, this.name);
  factory Person.fromJson(Map<String, dynamic> json) => Person(json['id'] as String, json['name'] as String);
  final String id;
  final String name;
}

class Comment {
  Comment(this.author, this.body, this.createdAt);
  factory Comment.fromJson(Map<String, dynamic> json) =>
      Comment(Person.fromJson(json['author']), json['body'] as String, parseUtc(json['creationTime']));
  final Person author;
  final String body;
  final DateTime createdAt;
}

class Ticket {
  Ticket({
    required this.id,
    required this.code,
    required this.title,
    required this.status,
    required this.priority,
    required this.category,
    required this.createdAt,
    this.assignee,
    this.description = '',
    this.resolution,
    this.waitingNote,
    this.actions = const [],
    this.comments = const [],
  });

  factory Ticket.fromJson(Map<String, dynamic> json) => Ticket(
        id: json['id'] as String,
        code: json['code'] as String,
        title: json['title'] as String,
        status: json['status'] as int,
        priority: json['priority'] as int,
        category: json['categoryName'] as String,
        createdAt: parseUtc(json['creationTime']),
        assignee: json['assignee'] == null ? null : Person.fromJson(json['assignee']),
        description: json['description'] as String? ?? '',
        resolution: json['resolution'] as String?,
        waitingNote: json['waitingNote'] as String?,
        actions: [for (final a in json['actions'] as List? ?? const []) a as int],
        comments: [for (final c in json['comments'] as List? ?? const []) Comment.fromJson(c)],
      );

  final String id;
  final String code;
  final String title;
  final int status;
  final int priority;
  final String category;
  final DateTime createdAt;
  final Person? assignee;
  final String description;
  final String? resolution;
  final String? waitingNote;
  final List<int> actions;
  final List<Comment> comments;

  String get statusName => statusNames[status] ?? '$status';
  String get priorityName => priorityNames[priority] ?? '$priority';

  /// The server decides what is allowed; this is the part of it a requester can use.
  bool can(int action) => actions.contains(action);
}

class Category {
  Category(this.id, this.name);
  final String id;
  final String name;
}

/// The API sends UTC times, sometimes without a trailing Z.
DateTime parseUtc(String value) =>
    DateTime.parse(RegExp(r'(Z|[+-]\d\d:?\d\d)$').hasMatch(value) ? value : '${value}Z').toLocal();

/// What a requester may do besides commenting: the action code, its route and the body field it needs.
enum Act {
  confirm(9, 'confirm', 'Confirm it is solved', null),
  reject(10, 'reject', 'Not solved', 'reason'),
  reopen(11, 'reopen', 'Reopen', 'reason'),
  cancel(12, 'cancel', 'Cancel ticket', 'reason');

  const Act(this.code, this.path, this.label, this.field);
  final int code;
  final String path;
  final String label;

  /// Null when the action needs no text.
  final String? field;
}

const commentAction = 1;

class ApiClient {
  ApiClient({http.Client? client, this.baseUrl = apiUrl}) : _http = client ?? http.Client();

  final http.Client _http;
  final String baseUrl;
  String? _token;

  /// Called when the server says the session is over.
  void Function()? onSignedOut;

  bool get signedIn => _token != null;

  Future<void> signIn(String username, String password) async {
    final response = await _send(() => _http.post(
          Uri.parse('$baseUrl/connect/token'),
          body: {
            'grant_type': 'password',
            'client_id': 'Helpdesk_Demo',
            'username': username,
            'password': password,
            'scope': 'Helpdesk',
          },
        ));
    final body = _decode(response);
    if (response.statusCode != 200) {
      throw ApiException(response.statusCode, (body['error_description'] as String?) ?? 'Invalid user name or password.');
    }
    _token = body['access_token'] as String;
  }

  void signOut() => _token = null;

  Future<List<Ticket>> myTickets() async {
    final body = await _get('/api/app/ticket/my-list', {'MaxResultCount': '50'});
    return [for (final item in body['items'] as List) Ticket.fromJson(item)];
  }

  Future<Ticket> ticket(String id) async => Ticket.fromJson(await _get('/api/app/ticket/$id'));

  Future<List<Category>> categories() async {
    final body = await _get('/api/app/category', {'ActiveOnly': 'true'});
    return [for (final item in body['items'] as List) Category(item['id'], item['name'])];
  }

  /// Category ids the history suggests for a new ticket, best first.
  Future<List<String>> suggestCategoryIds(String title, String description) async {
    final body = await _get('/api/app/ticket-triage/suggestion', {'Title': title, 'Description': description});
    return [for (final item in body['categories'] as List) item['categoryId'] as String];
  }

  Future<Ticket> create(String categoryId, String title, String description) async => Ticket.fromJson(
        await _post('/api/app/ticket', {'categoryId': categoryId, 'title': title, 'description': description}),
      );

  Future<void> comment(String id, String body) => _post('/api/app/ticket/$id/comment', {'body': body, 'visibility': 1});

  Future<void> act(String id, Act action, [String? text]) =>
      _post('/api/app/ticket/$id/${action.path}', action.field == null ? null : {action.field!: text});

  Future<Map<String, dynamic>> _get(String path, [Map<String, String>? query]) async {
    final uri = Uri.parse('$baseUrl$path').replace(queryParameters: query);
    return _checked(await _send(() => _http.get(uri, headers: _headers())));
  }

  Future<Map<String, dynamic>> _post(String path, Map<String, dynamic>? body) async {
    final uri = Uri.parse('$baseUrl$path');
    final payload = body == null ? null : jsonEncode(body);
    return _checked(await _send(() => _http.post(uri, headers: _headers(json: body != null), body: payload)));
  }

  Map<String, String> _headers({bool json = false}) => {
        if (_token != null) 'Authorization': 'Bearer $_token',
        if (json) 'Content-Type': 'application/json',
      };

  Future<http.Response> _send(Future<http.Response> Function() call) async {
    try {
      return await call();
    } on http.ClientException {
      throw ApiException(0, 'Cannot reach the server. Is the API running?');
    }
  }

  Map<String, dynamic> _checked(http.Response response) {
    if (response.statusCode == 401 && _token != null) {
      _token = null;
      onSignedOut?.call();
      throw ApiException(401, 'Your session has expired. Please sign in again.');
    }
    final body = _decode(response);
    if (response.statusCode >= 400) {
      final error = body['error'] as Map<String, dynamic>?;
      final details = (error?['validationErrors'] as List?)?.map((e) => e['message']).join(' ');
      final message = (details?.isNotEmpty ?? false) ? details! : (error?['message'] as String? ?? 'Request failed');
      throw ApiException(response.statusCode, message);
    }
    return body;
  }

  Map<String, dynamic> _decode(http.Response response) {
    try {
      final value = jsonDecode(utf8.decode(response.bodyBytes));
      return value is Map<String, dynamic> ? value : {};
    } on FormatException {
      return {};
    }
  }
}
