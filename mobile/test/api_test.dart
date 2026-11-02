import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:helpdesk_mobile/api.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

http.Response json(int status, Object body) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

const ticketJson = {
  'id': 't1',
  'code': 'HD-1',
  'title': 'VPN is down',
  'status': 3,
  'priority': 3,
  'categoryName': 'IT support',
  'creationTime': '2026-11-02T08:00:00',
  'assignee': {'id': 'a1', 'name': 'agent1'},
  'description': 'Cannot connect',
  'actions': [1, 12],
  'comments': [
    {
      'author': {'id': 'u1', 'name': 'alice'},
      'body': 'Still broken',
      'creationTime': '2026-11-02T09:00:00Z'
    }
  ],
};

void main() {
  attachmentTests();

  test('parseUtc reads a time without a zone as UTC', () {
    expect(parseUtc('2026-11-02T08:00:00').toUtc(), DateTime.utc(2026, 11, 2, 8));
    expect(parseUtc('2026-11-02T15:00:00+07:00').toUtc(), DateTime.utc(2026, 11, 2, 8));
  });

  test('Ticket.fromJson names the enums and exposes only the allowed actions', () {
    final ticket = Ticket.fromJson(ticketJson);
    expect(ticket.statusName, 'Waiting');
    expect(ticket.priorityName, 'High');
    expect(ticket.can(Act.cancel.code), isTrue);
    expect(ticket.can(Act.confirm.code), isFalse);
    expect(ticket.comments.single.author.name, 'alice');
  });

  group('ApiClient', () {
    test('signs in with the password grant and sends the token afterwards', () async {
      final seen = <http.Request>[];
      final api = ApiClient(
        baseUrl: 'http://api',
        client: MockClient((request) async {
          seen.add(request);
          if (request.url.path == '/connect/token') return json(200, {'access_token': 'tok'});
          return json(200, {'items': [], 'totalCount': 0});
        }),
      );
      await api.signIn('alice', 'pw');
      await api.myTickets();

      expect(seen.first.bodyFields['grant_type'], 'password');
      expect(seen.first.bodyFields['client_id'], 'Helpdesk_Demo');
      expect(seen.last.headers['Authorization'], 'Bearer tok');
      expect(seen.last.url.queryParameters['MaxResultCount'], '50');
    });

    test('reports bad credentials in the server\'s words', () async {
      final api = ApiClient(client: MockClient((_) async => json(400, {'error_description': 'Invalid username or password!'})));
      expect(api.signIn('alice', 'no'), throwsA(isA<ApiException>().having((e) => e.message, 'message', 'Invalid username or password!')));
    });

    test('joins ABP validation messages into one sentence', () async {
      final api = ApiClient(
        client: MockClient((_) async => json(400, {
              'error': {
                'message': 'Validation failed',
                'validationErrors': [
                  {'message': 'Title is required.'},
                  {'message': 'Too long.'}
                ]
              }
            })),
      );
      expect(api.create('c', '', ''), throwsA(isA<ApiException>().having((e) => e.message, 'message', 'Title is required. Too long.')));
    });

    test('a 401 with a token signs out and tells the app', () async {
      var signedOut = false;
      final api = ApiClient(
        client: MockClient((request) async =>
            request.url.path == '/connect/token' ? json(200, {'access_token': 'tok'}) : json(401, {})),
      )..onSignedOut = () => signedOut = true;
      await api.signIn('alice', 'pw');

      await expectLater(api.ticket('t1'), throwsA(isA<ApiException>().having((e) => e.status, 'status', 401)));
      expect(signedOut, isTrue);
      expect(api.signedIn, isFalse);
    });

    test('an action goes to its own route, with the reason only when it needs one', () async {
      final seen = <http.Request>[];
      final api = ApiClient(client: MockClient((request) async {
        seen.add(request);
        return json(200, ticketJson);
      }));

      await api.act('t1', Act.confirm);
      await api.act('t1', Act.reject, 'still broken');

      expect(seen[0].url.path, '/api/app/ticket/t1/confirm');
      expect(seen[0].body, isEmpty);
      expect(seen[1].url.path, '/api/app/ticket/t1/reject');
      expect(jsonDecode(seen[1].body), {'reason': 'still broken'});
    });

    test('a network failure becomes a readable message', () async {
      final api = ApiClient(client: MockClient((_) async => throw http.ClientException('refused')));
      expect(api.myTickets(), throwsA(isA<ApiException>().having((e) => e.status, 'status', 0)));
    });
  });
}

void attachmentTests() {
  group('attachments', () {
    test('attachmentProblem refuses what the server would refuse', () {
      expect(attachmentProblem('a.png', 100, 0), isNull);
      expect(attachmentProblem('A.JPG', 100, 4), isNull);
      expect(attachmentProblem('a.webp', maxAttachmentBytes, 0), isNull);
      expect(attachmentProblem('a.gif', 100, 0), 'attach.type');
      expect(attachmentProblem('noextension', 100, 0), 'attach.type');
      expect(attachmentProblem('a.png', 0, 0), 'attach.empty');
      expect(attachmentProblem('a.png', maxAttachmentBytes + 1, 0), 'attach.big');
      expect(attachmentProblem('a.png', 100, maxAttachments), 'attach.limit');
    });

    test('an upload is a multipart POST with the token, the ticket id and the file name', () async {
      http.BaseRequest? seen;
      String? body;
      final api = ApiClient(
        baseUrl: 'http://api',
        client: MockClient((request) async {
          if (request.url.path == '/connect/token') return json(200, {'access_token': 'tok'});
          seen = request;
          body = request.body;
          return json(200, {
            'id': 'a1', 'fileName': 'shot.png', 'size': 3, 'visibility': 1, 'uploader': {'id': 'u', 'name': 'alice'},
          });
        }),
      );
      await api.signIn('alice', 'pw');

      final saved = await api.uploadAttachment('t1', 'shot.png', Uint8List.fromList([1, 2, 3]));

      expect(saved.fileName, 'shot.png');
      expect(seen!.method, 'POST');
      expect(seen!.url.path, '/api/app/ticket-attachment/upload/t1');
      expect(seen!.url.queryParameters['visibility'], '1');
      expect(seen!.headers['Authorization'], 'Bearer tok');
      expect(seen!.headers['content-type'], startsWith('multipart/form-data'));
      expect(body, contains('name="file"'));
      expect(body, contains('filename="shot.png"'));
    });

    test('the list and the bytes come from their own routes', () async {
      final api = ApiClient(client: MockClient((request) async {
        if (request.url.path == '/api/app/ticket-attachment') {
          expect(request.url.queryParameters['ticketId'], 't1');
          return json(200, {
            'items': [
              {'id': 'a1', 'fileName': 'x.png', 'size': 10, 'visibility': 2, 'uploader': {'id': 'u', 'name': 'agent1'}},
            ]
          });
        }
        return http.Response.bytes([9, 8, 7], 200, headers: {'content-type': 'image/png'});
      }));

      final list = await api.attachments('t1');
      expect(list.single.internal, isTrue);
      expect(list.single.uploader, 'agent1');
      expect(await api.attachmentBytes('a1'), [9, 8, 7]);
    });

    test('a refused file reports the server message, and a missing image is an error, not empty bytes', () async {
      final refusing = ApiClient(client: MockClient((_) async => json(415, {
            'error': {'message': 'Only PNG, JPEG and WebP images can be attached.'}
          })));
      await expectLater(
        refusing.uploadAttachment('t1', 'x.png', Uint8List.fromList([1])),
        throwsA(isA<ApiException>().having((e) => e.status, 'status', 415).having((e) => e.message, 'message', contains('Only PNG'))),
      );

      final missing = ApiClient(client: MockClient((_) async => json(404, {'error': {'message': 'Not found'}})));
      await expectLater(missing.attachmentBytes('nope'), throwsA(isA<ApiException>().having((e) => e.status, 'status', 404)));
    });
  });
}
