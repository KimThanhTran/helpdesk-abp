import 'package:flutter/material.dart';

import 'api.dart';

/// Every text the app shows, in the languages it offers. English is the fallback for a missing key.
const Map<String, Map<String, String>> texts = {
  'en': {
    'language': 'Language',
    'login.user': 'User name',
    'login.password': 'Password',
    'login.submit': 'Sign in',
    'login.busy': 'Signing in…',
    'login.demo': 'Fill demo account (alice)',
    'nav.signOut': 'Sign out',
    'tickets.title': 'My tickets',
    'tickets.new': 'New ticket',
    'tickets.none': 'No tickets yet.',
    'new.title': 'Title',
    'new.what': 'What is going on?',
    'new.category': 'Category',
    'new.looksLike': 'Looks like: {name}',
    'new.send': 'Send',
    'new.sending': 'Sending…',
    'new.fill': 'Fill in the title and the description.',
    'ticket.title': 'Ticket',
    'ticket.assigned': 'Assigned to {name} · opened {date}',
    'ticket.nobody': 'nobody yet',
    'ticket.waiting': 'Waiting for you: {note}',
    'ticket.resolution': 'Resolution: {text}',
    'images.title': 'Images',
    'images.none': 'No images yet.',
    'images.add': 'Add an image',
    'images.internal': 'internal',
    'conversation.title': 'Conversation',
    'conversation.none': 'No comments yet.',
    'comment.add': 'Add a comment',
    'comment.send': 'Send',
    'dialog.why': 'Why?',
    'dialog.nevermind': 'Never mind',
    'dialog.confirm': 'Confirm',
    'act.confirm': 'Confirm it is solved',
    'act.reject': 'Not solved',
    'act.reopen': 'Reopen',
    'act.cancel': 'Cancel ticket',
    'status.1': 'New',
    'status.2': 'Open',
    'status.3': 'Waiting',
    'status.4': 'Resolved',
    'status.5': 'Closed',
    'status.6': 'Cancelled',
    'priority.1': 'Low',
    'priority.2': 'Normal',
    'priority.3': 'High',
    'priority.4': 'Urgent',
    'attach.limit': 'A ticket can hold at most {max} images.',
    'attach.type': 'Only PNG, JPEG and WebP images can be attached.',
    'attach.big': 'The image is too large. The limit is 5 MB.',
    'attach.empty': 'The file is empty.',
    'error.network': 'Cannot reach the server. Is the API running?',
    'error.expired': 'Your session has expired. Please sign in again.',
    'error.login': 'Invalid user name or password.',
    'error.failed': 'Request failed',
  },
  'vi': {
    'language': 'Ngôn ngữ',
    'login.user': 'Tên đăng nhập',
    'login.password': 'Mật khẩu',
    'login.submit': 'Đăng nhập',
    'login.busy': 'Đang đăng nhập…',
    'login.demo': 'Điền tài khoản demo (alice)',
    'nav.signOut': 'Đăng xuất',
    'tickets.title': 'Phiếu của tôi',
    'tickets.new': 'Tạo phiếu mới',
    'tickets.none': 'Chưa có phiếu nào.',
    'new.title': 'Tiêu đề',
    'new.what': 'Bạn đang gặp vấn đề gì?',
    'new.category': 'Danh mục',
    'new.looksLike': 'Có vẻ là: {name}',
    'new.send': 'Gửi',
    'new.sending': 'Đang gửi…',
    'new.fill': 'Vui lòng nhập tiêu đề và mô tả.',
    'ticket.title': 'Phiếu',
    'ticket.assigned': 'Giao cho {name} · tạo lúc {date}',
    'ticket.nobody': 'chưa có ai',
    'ticket.waiting': 'Đang chờ bạn phản hồi: {note}',
    'ticket.resolution': 'Cách giải quyết: {text}',
    'images.title': 'Ảnh',
    'images.none': 'Chưa có ảnh nào.',
    'images.add': 'Thêm ảnh',
    'images.internal': 'nội bộ',
    'conversation.title': 'Trao đổi',
    'conversation.none': 'Chưa có bình luận.',
    'comment.add': 'Thêm bình luận',
    'comment.send': 'Gửi',
    'dialog.why': 'Vì sao?',
    'dialog.nevermind': 'Bỏ qua',
    'dialog.confirm': 'Xác nhận',
    'act.confirm': 'Xác nhận đã giải quyết',
    'act.reject': 'Chưa giải quyết',
    'act.reopen': 'Mở lại',
    'act.cancel': 'Hủy phiếu',
    'status.1': 'Mới',
    'status.2': 'Đang xử lý',
    'status.3': 'Đang chờ phản hồi',
    'status.4': 'Đã giải quyết',
    'status.5': 'Đã đóng',
    'status.6': 'Đã hủy',
    'priority.1': 'Thấp',
    'priority.2': 'Bình thường',
    'priority.3': 'Cao',
    'priority.4': 'Khẩn cấp',
    'attach.limit': 'Mỗi phiếu chỉ chứa tối đa {max} ảnh.',
    'attach.type': 'Chỉ có thể đính kèm ảnh PNG, JPEG và WebP.',
    'attach.big': 'Ảnh quá lớn. Giới hạn là 5 MB.',
    'attach.empty': 'Tệp trống.',
    'error.network': 'Không kết nối được máy chủ. API có đang chạy không?',
    'error.expired': 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
    'error.login': 'Sai tên đăng nhập hoặc mật khẩu.',
    'error.failed': 'Yêu cầu không thành công',
  },
};

const languages = {'en': 'English', 'vi': 'Tiếng Việt'};

/// The language in use. Changing it rebuilds every screen that asked for [Strings.of].
class LanguageController extends ChangeNotifier {
  LanguageController(this._language);

  String _language;

  String get language => _language;

  set language(String value) {
    if (!texts.containsKey(value) || value == _language) return;
    _language = value;
    notifyListeners();
  }

  /// Vietnamese when the phone is set to Vietnamese, English otherwise.
  static String fromDevice(Locale locale) => locale.languageCode == 'vi' ? 'vi' : 'en';
}

class Strings {
  const Strings(this.language);

  final String language;

  /// A text by key, with `{name}` placeholders filled in; the key itself when nothing has it.
  String t(String key, [Map<String, String> params = const {}]) {
    var text = texts[language]?[key] ?? texts['en']![key] ?? key;
    params.forEach((name, value) => text = text.replaceAll('{$name}', value));
    return text;
  }

  String status(int value) => t('status.$value');
  String priority(int value) => t('priority.$value');

  /// The message for an error: the app's own wording for what it knows, the server's message as it came otherwise.
  String error(Object? error) {
    if (error is ApiException) return error.kind == null ? error.message : t('error.${error.kind}');
    return error == null ? '' : '$error';
  }

  /// The text for the reason [attachmentProblem] gave.
  String problem(String key) => t(key, {'max': '$maxAttachments'});

  static Strings of(BuildContext context) =>
      Strings(context.dependOnInheritedWidgetOfExactType<StringsScope>()?.notifier?.language ?? 'en');
}

class StringsScope extends InheritedNotifier<LanguageController> {
  const StringsScope({super.key, required LanguageController controller, required super.child}) : super(notifier: controller);
}

/// The language button for an app bar.
class LanguageMenu extends StatelessWidget {
  const LanguageMenu({super.key});

  @override
  Widget build(BuildContext context) {
    final controller = context.dependOnInheritedWidgetOfExactType<StringsScope>()!.notifier!;
    return PopupMenuButton<String>(
      key: const Key('language'),
      icon: const Icon(Icons.translate),
      tooltip: Strings.of(context).t('language'),
      initialValue: controller.language,
      onSelected: (value) => controller.language = value,
      itemBuilder: (_) => [
        for (final entry in languages.entries) PopupMenuItem(key: Key('language-${entry.key}'), value: entry.key, child: Text(entry.value)),
      ],
    );
  }
}
