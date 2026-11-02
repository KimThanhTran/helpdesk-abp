import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:helpdesk_mobile/api.dart';
import 'package:helpdesk_mobile/main.dart';
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

  Future<http.Response> handle(http.Request request) async {
    final path = request.url.path;
    if (request.method == 'POST') posts.add(path);
    switch (path) {
      case '/connect/token':
        return request.bodyFields['password'] == 'good'
            ? json(200, {'access_token': 'tok'})
            : json(400, {'error_description': 'Invalid username or password!'});
      case '/api/app/ticket/my-list':
        return json(200, {'items': [ticket(status: status)], 'totalCount': 1});
      case '/api/app/ticket/t1':
        return json(200, ticket(status: status, actions: status == 4 ? [1, 9, 10] : [1, 11]));
      case '/api/app/ticket/t1/confirm':
        status = 5;
        return json(200, ticket(status: 5));
    }
    return json(404, {});
  }
}

Future<Fake> start(WidgetTester tester) async {
  final fake = Fake();
  await tester.pumpWidget(HelpdeskApp(api: ApiClient(baseUrl: 'http://api', client: MockClient(fake.handle))));
  return fake;
}

Future<void> signIn(WidgetTester tester, String password) async {
  await tester.enterText(find.byKey(const Key('user')), 'alice');
  await tester.enterText(find.byKey(const Key('password')), password);
  await tester.tap(find.text('Sign in'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('a wrong password shows the server message and stays on the sign-in page', (tester) async {
    await start(tester);
    await signIn(tester, 'bad');
    expect(find.text('Invalid username or password!'), findsOneWidget);
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
}
