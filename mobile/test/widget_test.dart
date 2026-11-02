import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:helpdesk_mobile/api.dart';
import 'package:helpdesk_mobile/main.dart';
import 'package:helpdesk_mobile/pages.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

http.Response json(int status, Object body) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

Map<String, Object?> ticket({int status = 4, List<int> actions = const [1, 9, 10]}) => {
      'id': 't1',
      'code': 'HD-1',
      'title': 'VPN is down',
      'status': status,
      'priority': 2,
      'categoryName': 'IT support',
      'creationTime': '2026-11-02T08:00:00Z',
      'description': 'Cannot connect',
      'resolution': 'Restarted the gateway',
      'actions': actions,
      'comments': [],
    };

/// A tiny fake server that remembers what was sent, so the app can be driven end to end.
class Fake {
  int status = 4;
  final posts = <String>[];
  final images = <String>[]; // names of the attachments on the ticket
  final languages = <String>[]; // the Accept-Language of each request

  Future<http.Response> handle(http.Request request) async {
    final path = request.url.path;
    languages.add(request.headers['Accept-Language'] ?? '');
    if (request.method == 'POST') posts.add(path);
    if (path == '/api/app/ticket-attachment') {
      return json(200, {
        'items': [
          for (final (i, name) in images.indexed)
            {'id': 'a$i', 'fileName': name, 'size': 3, 'visibility': 1, 'uploader': {'id': 'u', 'name': 'alice'}},
        ]
      });
    }
    if (path.startsWith('/api/app/ticket-attachment/upload/')) {
      images.add(RegExp('filename="([^"]+)"').firstMatch(request.body)![1]!);
      return json(200, {'id': 'new', 'fileName': images.last, 'size': 3, 'visibility': 1, 'uploader': {'id': 'u', 'name': 'alice'}});
    }
    if (path.startsWith('/api/app/ticket-attachment/') && path.endsWith('/content')) {
      return http.Response.bytes([1, 2, 3], 200, headers: {'content-type': 'image/png'});
    }
    switch (path) {
      case '/connect/token':
        return request.bodyFields['password'] == 'good'
            ? json(200, {'access_token': 'tok'})
            : json(400, {'error_description': 'Invalid username or password!'});
      case '/api/app/ticket/my-list':
        return json(200, {'items': [ticket(status: status)], 'totalCount': 1});
      case '/api/app/ticket/t1':
        return json(200, ticket(status: status, actions: status == 4 ? [1, 9, 10] : [11]));
      case '/api/app/ticket/t1/confirm':
        status = 5;
        return json(200, ticket(status: 5));
    }
    return json(404, {});
  }
}

Future<Fake> start(WidgetTester tester, {PickImage? pick, String? language}) async {
  final fake = Fake();
  await tester.pumpWidget(HelpdeskApp(
    api: ApiClient(baseUrl: 'http://api', client: MockClient(fake.handle)),
    pickImage: pick ?? () async => null,
    language: language,
  ));
  return fake;
}

PickedImage picked(String name, int size) => PickedImage(name, size, () async => Uint8List.fromList(List.filled(size, 1)));

Future<void> openTicket(WidgetTester tester) async {
  await signIn(tester, 'good');
  await tester.tap(find.byKey(const Key('ticket-HD-1')));
  await tester.pumpAndSettle();
}

Future<void> signIn(WidgetTester tester, String password) async {
  await tester.enterText(find.byKey(const Key('user')), 'alice');
  await tester.enterText(find.byKey(const Key('password')), password);
  await tester.tap(find.byType(FilledButton)); // the sign-in button, whatever language it is written in
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('a wrong password shows the server message and stays on the sign-in page', (tester) async {
    await start(tester);
    await signIn(tester, 'bad');
    expect(find.text('Invalid user name or password.'), findsOneWidget); // the app's own wording, not the server's
    expect(find.text('My tickets'), findsNothing);
  });

  testWidgets('sign in, open a resolved ticket and confirm it', (tester) async {
    final fake = await start(tester);
    await signIn(tester, 'good');
    expect(find.text('HD-1  VPN is down'), findsOneWidget);

    await tester.tap(find.byKey(const Key('ticket-HD-1')));
    await tester.pumpAndSettle();
    expect(find.text('Resolved'), findsOneWidget);
    expect(find.byKey(const Key('act-confirm')), findsOneWidget);
    expect(find.byKey(const Key('act-cancel')), findsNothing); // the server did not allow it

    await tester.tap(find.byKey(const Key('act-confirm')));
    await tester.pumpAndSettle();
    expect(fake.posts, contains('/api/app/ticket/t1/confirm'));
    expect(find.text('Closed'), findsOneWidget);
    expect(find.byKey(const Key('act-reopen')), findsOneWidget);
  });

  testWidgets('signing out returns to the sign-in page', (tester) async {
    await start(tester);
    await signIn(tester, 'good');
    await tester.tap(find.byTooltip('Sign out'));
    await tester.pumpAndSettle();
    expect(find.text('Sign in'), findsOneWidget);
  });

  group('images', () {
    testWidgets('a chosen PNG is uploaded and shows up on the ticket', (tester) async {
      final fake = await start(tester, pick: () async => picked('shot.png', 3));
      await openTicket(tester);
      expect(find.text('No images yet.'), findsOneWidget);

      await tester.tap(find.byKey(const Key('add-image')));
      await tester.pumpAndSettle();

      expect(fake.posts, contains('/api/app/ticket-attachment/upload/t1'));
      expect(find.text('shot.png'), findsOneWidget);
      expect(find.text('No images yet.'), findsNothing);
    });

    testWidgets('a file that is not an image is refused on the phone, with no upload', (tester) async {
      final fake = await start(tester, pick: () async => picked('notes.txt', 3));
      await openTicket(tester);

      await tester.tap(find.byKey(const Key('add-image')));
      await tester.pumpAndSettle();

      expect(find.text('Only PNG, JPEG and WebP images can be attached.'), findsOneWidget);
      expect(fake.posts.where((p) => p.contains('upload')), isEmpty);
    });

    testWidgets('a file over 5 MB is refused before its bytes are read', (tester) async {
      var read = false;
      final fake = await start(tester, pick: () async => PickedImage('huge.png', maxAttachmentBytes + 1, () async {
            read = true;
            return Uint8List(0);
          }));
      await openTicket(tester);

      await tester.tap(find.byKey(const Key('add-image')));
      await tester.pumpAndSettle();

      expect(find.text('The image is too large. The limit is 5 MB.'), findsOneWidget);
      expect(read, isFalse);
      expect(fake.posts.where((p) => p.contains('upload')), isEmpty);
    });

    testWidgets('cancelling the chooser changes nothing', (tester) async {
      final fake = await start(tester, pick: () async => null);
      await openTicket(tester);

      await tester.tap(find.byKey(const Key('add-image')));
      await tester.pumpAndSettle();

      expect(fake.posts.where((p) => p.contains('upload')), isEmpty);
    });

    testWidgets('the button stops at five images and a closed ticket offers none', (tester) async {
      final fake = await start(tester, pick: () async => picked('x.png', 3));
      fake.images.addAll(['1.png', '2.png', '3.png', '4.png', '5.png']);
      await openTicket(tester);

      expect(tester.widget<TextButton>(find.byKey(const Key('add-image'))).onPressed, isNull);

      // a confirmed ticket takes no more comments, so no more images
      await tester.tap(find.byKey(const Key('act-confirm')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('add-image')), findsNothing);
      expect(find.text('1.png'), findsOneWidget); // what is there stays visible
    });
  });

  group('language', () {
    testWidgets('the sign-in page can be switched to Vietnamese and back', (tester) async {
      await start(tester);
      expect(find.text('Sign in'), findsOneWidget);

      await tester.tap(find.byKey(const Key('language')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('language-vi')));
      await tester.pumpAndSettle();

      expect(find.text('Đăng nhập'), findsOneWidget);
      expect(find.text('Tên đăng nhập'), findsOneWidget);
      expect(find.text('Sign in'), findsNothing);

      await tester.tap(find.byKey(const Key('language')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('language-en')));
      await tester.pumpAndSettle();
      expect(find.text('Sign in'), findsOneWidget);
    });

    testWidgets('statuses, buttons and errors follow the language, and the server is asked in it too', (tester) async {
      final fake = await start(tester, language: 'vi');
      await signIn(tester, 'bad');
      expect(find.text('Sai tên đăng nhập hoặc mật khẩu.'), findsOneWidget);
      expect(fake.languages.last, 'vi'); // the sign-in request carried Accept-Language

      await signIn(tester, 'good');
      expect(find.text('Phiếu của tôi'), findsOneWidget);
      expect(find.textContaining('Đã giải quyết'), findsOneWidget);

      await tester.tap(find.byKey(const Key('ticket-HD-1')));
      await tester.pumpAndSettle();
      expect(find.text('Đã giải quyết'), findsOneWidget);
      expect(find.text('Xác nhận đã giải quyết'), findsOneWidget);
      expect(find.text('Chưa có ảnh nào.'), findsOneWidget);
      expect(fake.languages.last, 'vi');
    });

    testWidgets('a language chosen on one screen applies to the screens already open', (tester) async {
      await start(tester);
      await openTicket(tester);
      expect(find.text('Confirm it is solved'), findsOneWidget);

      await tester.tap(find.byKey(const Key('language')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('language-vi')));
      await tester.pumpAndSettle();

      expect(find.text('Xác nhận đã giải quyết'), findsOneWidget);
      expect(find.text('Confirm it is solved'), findsNothing);
    });
  });
}
