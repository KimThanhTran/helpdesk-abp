import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:helpdesk_mobile/api.dart';
import 'package:helpdesk_mobile/strings.dart';

Set<String> placeholders(String text) => RegExp(r'\{(\w+)\}').allMatches(text).map((m) => m[1]!).toSet();

void main() {
  test('every language has exactly the same texts', () {
    expect(texts['vi']!.keys.toSet(), texts['en']!.keys.toSet());
  });

  test('a translation keeps the placeholders of the English text', () {
    for (final key in texts['en']!.keys) {
      expect(placeholders(texts['vi']![key]!), placeholders(texts['en']![key]!), reason: key);
    }
  });

  test('a translation is not just the English text', () {
    final same = texts['en']!.keys.where((key) => texts['vi']![key] == texts['en']![key]).toList();
    expect(same, isEmpty);
  });

  test('every status and priority the server can send has a name', () {
    for (final key in [for (var i = 1; i <= 6; i++) 'status.$i', for (var i = 1; i <= 4; i++) 'priority.$i']) {
      expect(texts['vi']![key], isNotNull, reason: key);
    }
    for (final act in Act.values) {
      expect(texts['vi']!['act.${act.path}'], isNotNull, reason: act.path);
    }
  });

  group('Strings', () {
    const en = Strings('en');
    const vi = Strings('vi');

    test('fills placeholders, falls back to English and then to the key', () {
      expect(vi.t('ticket.waiting', {'note': 'gửi log'}), 'Đang chờ bạn phản hồi: gửi log');
      expect(en.t('attach.limit', {'max': '5'}), 'A ticket can hold at most 5 images.');
      expect(const Strings('xx').t('login.submit'), 'Sign in'); // an unknown language reads as English
      expect(en.t('no.such.key'), 'no.such.key');
    });

    test('words the app\'s own errors and shows the server\'s as they came', () {
      expect(vi.error(ApiException(0, 'Cannot reach the server.', 'network')), contains('Không kết nối được'));
      expect(en.error(ApiException(401, 'x', 'expired')), contains('expired'));
      expect(vi.error(ApiException(403, 'Vai trò của bạn không được phép.')), 'Vai trò của bạn không được phép.');
      expect(vi.error(null), '');
    });

    test('words a refused attachment', () {
      expect(vi.problem('attach.limit'), 'Mỗi phiếu chỉ chứa tối đa 5 ảnh.');
      expect(en.problem('attach.type'), 'Only PNG, JPEG and WebP images can be attached.');
    });

    test('names statuses and priorities', () {
      expect(vi.status(3), 'Đang chờ phản hồi');
      expect(en.priority(4), 'Urgent');
    });
  });

  group('LanguageController', () {
    test('starts from the device language: Vietnamese for vi, English for anything else', () {
      expect(LanguageController.fromDevice(const Locale('vi', 'VN')), 'vi');
      expect(LanguageController.fromDevice(const Locale('fr')), 'en');
      expect(LanguageController.fromDevice(const Locale('en', 'US')), 'en');
    });

    test('ignores a language it does not have and tells listeners only about a real change', () {
      final controller = LanguageController('en');
      var calls = 0;
      controller.addListener(() => calls++);

      controller.language = 'klingon';
      controller.language = 'en';
      expect(calls, 0);

      controller.language = 'vi';
      expect(controller.language, 'vi');
      expect(calls, 1);
    });
  });
}
