import 'dart:async';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import 'api.dart';

String formatTime(DateTime time) {
  String two(int n) => n.toString().padLeft(2, '0');
  return '${time.year}-${two(time.month)}-${two(time.day)} ${two(time.hour)}:${two(time.minute)}';
}

/// An image the person chose. The bytes are read only after the size has passed the check.
class PickedImage {
  PickedImage(this.name, this.size, this.read);
  final String name;
  final int size;
  final Future<Uint8List> Function() read;
}

typedef PickImage = Future<PickedImage?> Function();

/// The system file chooser, limited to images.
Future<PickedImage?> pickImageFile() async {
  final file = await FilePicker.pickFile(type: FileType.image);
  if (file == null) return null;
  return PickedImage(file.name, await file.length() ?? 0, file.readAsBytes);
}

/// Shows an error under a form; empty when there is none.
class ErrorText extends StatelessWidget {
  const ErrorText(this.message, {super.key});
  final String? message;

  @override
  Widget build(BuildContext context) => message == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.symmetric(vertical: 8),
          child: Text(message!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
        );
}

class LoginPage extends StatefulWidget {
  const LoginPage({super.key, required this.api, required this.onSignedIn});
  final ApiClient api;
  final VoidCallback onSignedIn;

  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final _user = TextEditingController();
  final _password = TextEditingController();
  String? _error;
  bool _busy = false;

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.signIn(_user.text.trim(), _password.text);
      widget.onSignedIn();
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _busy = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 380),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Text('Helpdesk', style: Theme.of(context).textTheme.headlineMedium),
              const SizedBox(height: 24),
              TextField(key: const Key('user'), controller: _user, decoration: const InputDecoration(labelText: 'User name')),
              const SizedBox(height: 12),
              TextField(
                key: const Key('password'),
                controller: _password,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Password'),
                onSubmitted: (_) => _submit(),
              ),
              ErrorText(_error),
              const SizedBox(height: 12),
              FilledButton(onPressed: _busy ? null : _submit, child: Text(_busy ? 'Signing in…' : 'Sign in')),
              TextButton(
                onPressed: () {
                  _user.text = 'alice';
                  _password.text = '1q2w3E*';
                },
                child: const Text('Fill demo account (alice)'),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}

class TicketsPage extends StatefulWidget {
  const TicketsPage({super.key, required this.api, required this.onSignOut, this.pickImage = pickImageFile});
  final ApiClient api;
  final PickImage pickImage;
  final VoidCallback onSignOut;

  @override
  State<TicketsPage> createState() => _TicketsPageState();
}

class _TicketsPageState extends State<TicketsPage> {
  late Future<List<Ticket>> _tickets = widget.api.myTickets();

  Future<void> _refresh() async {
    setState(() {
      _tickets = widget.api.myTickets();
    });
    await _tickets.catchError((_) => <Ticket>[]);
  }

  Future<void> _open(Widget page) async {
    await Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => page));
    if (mounted) _refresh();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('My tickets'), actions: [
        IconButton(tooltip: 'Sign out', icon: const Icon(Icons.logout), onPressed: widget.onSignOut),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _open(NewTicketPage(api: widget.api)),
        icon: const Icon(Icons.add),
        label: const Text('New ticket'),
      ),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: FutureBuilder<List<Ticket>>(
          future: _tickets,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
            if (snapshot.hasError) {
              return ListView(children: [Padding(padding: const EdgeInsets.all(24), child: ErrorText('${snapshot.error}'))]);
            }
            final tickets = snapshot.data!;
            if (tickets.isEmpty) return ListView(children: const [Padding(padding: EdgeInsets.all(24), child: Text('No tickets yet.'))]);
            return ListView.separated(
              itemCount: tickets.length,
              separatorBuilder: (_, _) => const Divider(height: 1),
              itemBuilder: (context, index) {
                final ticket = tickets[index];
                return ListTile(
                  key: Key('ticket-${ticket.code}'),
                  title: Text('${ticket.code}  ${ticket.title}', maxLines: 2, overflow: TextOverflow.ellipsis),
                  subtitle: Text('${ticket.statusName} · ${ticket.priorityName} · ${ticket.category}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => _open(TicketPage(api: widget.api, id: ticket.id, pickImage: widget.pickImage)),
                );
              },
            );
          },
        ),
      ),
    );
  }
}

class NewTicketPage extends StatefulWidget {
  const NewTicketPage({super.key, required this.api});
  final ApiClient api;

  @override
  State<NewTicketPage> createState() => _NewTicketPageState();
}

class _NewTicketPageState extends State<NewTicketPage> {
  final _title = TextEditingController();
  final _description = TextEditingController();
  List<Category> _categories = [];
  String? _categoryId;
  String? _suggestedId;
  String? _error;
  bool _busy = false;
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    widget.api.categories().then((list) {
      if (mounted) setState(() => _categories = list);
    }).catchError((Object e) {
      if (mounted) setState(() => _error = '$e');
    });
  }

  @override
  void dispose() {
    _debounce?.cancel();
    super.dispose();
  }

  /// Asks which category similar past tickets were in, once the person stops typing. A failure is simply no hint.
  void _typed(String _) {
    _debounce?.cancel();
    if (_title.text.trim().length < 8) return;
    _debounce = Timer(const Duration(milliseconds: 600), () async {
      try {
        final ids = await widget.api.suggestCategoryIds(_title.text, _description.text);
        if (mounted) setState(() => _suggestedId = ids.isEmpty ? null : ids.first);
      } on ApiException {
        // no hint
      }
    });
  }

  Future<void> _send() async {
    final categoryId = _categoryId ?? (_categories.isEmpty ? null : _categories.first.id);
    if (categoryId == null || _title.text.trim().isEmpty || _description.text.trim().isEmpty) {
      setState(() => _error = 'Fill in the title and the description.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.api.create(categoryId, _title.text.trim(), _description.text.trim());
      if (mounted) Navigator.of(context).pop();
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _busy = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final suggested = _categories.where((c) => c.id == _suggestedId).firstOrNull;
    return Scaffold(
      appBar: AppBar(title: const Text('New ticket')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        TextField(key: const Key('title'), controller: _title, onChanged: _typed, decoration: const InputDecoration(labelText: 'Title')),
        const SizedBox(height: 12),
        TextField(
          key: const Key('description'),
          controller: _description,
          onChanged: _typed,
          minLines: 4,
          maxLines: 8,
          decoration: const InputDecoration(labelText: 'What is going on?', alignLabelWithHint: true),
        ),
        const SizedBox(height: 12),
        DropdownButtonFormField<String>(
          key: const Key('category'),
          initialValue: _categoryId ?? (_categories.isEmpty ? null : _categories.first.id),
          decoration: const InputDecoration(labelText: 'Category'),
          items: [for (final c in _categories) DropdownMenuItem(value: c.id, child: Text(c.name))],
          onChanged: (value) => setState(() => _categoryId = value),
        ),
        if (suggested != null && suggested.id != (_categoryId ?? _categories.first.id))
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(onPressed: () => setState(() => _categoryId = suggested.id), child: Text('Looks like: ${suggested.name}')),
          ),
        ErrorText(_error),
        const SizedBox(height: 12),
        FilledButton(onPressed: _busy ? null : _send, child: Text(_busy ? 'Sending…' : 'Send')),
      ]),
    );
  }
}

class TicketPage extends StatefulWidget {
  const TicketPage({super.key, required this.api, required this.id, this.pickImage = pickImageFile});
  final ApiClient api;
  final String id;
  final PickImage pickImage;

  @override
  State<TicketPage> createState() => _TicketPageState();
}

class _TicketPageState extends State<TicketPage> {
  late Future<Ticket> _ticket = widget.api.ticket(widget.id);
  late Future<List<Attachment>> _images = widget.api.attachments(widget.id);
  final _comment = TextEditingController();
  String? _error;
  bool _busy = false;

  void _reload() {
    setState(() {
      _ticket = widget.api.ticket(widget.id);
      _images = widget.api.attachments(widget.id);
    });
  }

  Future<void> _addImage(int alreadyAttached) async {
    final picked = await widget.pickImage();
    if (picked == null) return;
    final problem = attachmentProblem(picked.name, picked.size, alreadyAttached);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    await _run(() async {
      await widget.api.uploadAttachment(widget.id, picked.name, await picked.read());
    });
  }

  /// Runs a request, shows the server's refusal if there is one, and reloads the ticket on success.
  Future<void> _run(Future<void> Function() request) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await request();
      _reload();
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _act(Ticket ticket, Act action) async {
    String? text;
    if (action.field != null) {
      text = await _ask(action.label);
      if (text == null) return;
    }
    await _run(() => widget.api.act(ticket.id, action, text));
  }

  Future<String?> _ask(String title) {
    final controller = TextEditingController();
    return showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: TextField(key: const Key('reason'), controller: controller, autofocus: true, maxLines: 3, decoration: const InputDecoration(labelText: 'Why?')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Never mind')),
          FilledButton(
            onPressed: () => controller.text.trim().isEmpty ? null : Navigator.pop(context, controller.text.trim()),
            child: const Text('Confirm'),
          ),
        ],
      ),
    );
  }

  Future<void> _sendComment(Ticket ticket) async {
    final text = _comment.text.trim();
    if (text.isEmpty) return;
    await _run(() async {
      await widget.api.comment(ticket.id, text);
      _comment.clear();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Ticket')),
      body: FutureBuilder<Ticket>(
        future: _ticket,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (snapshot.hasError) return Padding(padding: const EdgeInsets.all(24), child: ErrorText('${snapshot.error}'));
          final ticket = snapshot.data!;
          final textTheme = Theme.of(context).textTheme;
          return ListView(padding: const EdgeInsets.all(16), children: [
            Text('${ticket.code}  ${ticket.title}', style: textTheme.titleLarge),
            const SizedBox(height: 8),
            Wrap(spacing: 8, children: [
              Chip(label: Text(ticket.statusName, key: const Key('status'))),
              Chip(label: Text(ticket.priorityName)),
              Chip(label: Text(ticket.category)),
            ]),
            Text('Assigned to ${ticket.assignee?.name ?? 'nobody yet'} · opened ${formatTime(ticket.createdAt)}'),
            if (ticket.waitingNote != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text('Waiting for you: ${ticket.waitingNote}')),
            const SizedBox(height: 12),
            Text(ticket.description),
            if (ticket.resolution != null)
              Card(margin: const EdgeInsets.only(top: 12), child: Padding(padding: const EdgeInsets.all(12), child: Text('Resolution: ${ticket.resolution}'))),
            const SizedBox(height: 12),
            Wrap(spacing: 8, children: [
              for (final action in Act.values)
                if (ticket.can(action.code))
                  OutlinedButton(key: Key('act-${action.path}'), onPressed: _busy ? null : () => _act(ticket, action), child: Text(action.label)),
            ]),
            ErrorText(_error),
            const Divider(height: 32),
            Text('Images', style: textTheme.titleMedium),
            FutureBuilder<List<Attachment>>(
              future: _images,
              builder: (context, images) {
                final items = images.data ?? const <Attachment>[];
                return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  if (images.hasError) ErrorText('${images.error}'),
                  if (images.connectionState == ConnectionState.done && items.isEmpty)
                    const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('No images yet.')),
                  Wrap(spacing: 8, runSpacing: 8, children: [
                    for (final item in items) AttachmentTile(key: Key('attachment-${item.id}'), api: widget.api, attachment: item),
                  ]),
                  if (ticket.can(commentAction))
                    TextButton.icon(
                      key: const Key('add-image'),
                      onPressed: _busy || items.length >= maxAttachments ? null : () => _addImage(items.length),
                      icon: const Icon(Icons.add_photo_alternate_outlined),
                      label: const Text('Add an image'),
                    ),
                ]);
              },
            ),
            const Divider(height: 32),
            Text('Conversation', style: textTheme.titleMedium),
            if (ticket.comments.isEmpty) const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('No comments yet.')),
            for (final comment in ticket.comments)
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(comment.body),
                subtitle: Text('${comment.author.name} · ${formatTime(comment.createdAt)}'),
              ),
            if (ticket.can(commentAction)) ...[
              TextField(key: const Key('comment'), controller: _comment, minLines: 2, maxLines: 5, decoration: const InputDecoration(labelText: 'Add a comment')),
              const SizedBox(height: 8),
              Align(
                alignment: Alignment.centerRight,
                child: FilledButton(key: const Key('send-comment'), onPressed: _busy ? null : () => _sendComment(ticket), child: const Text('Send')),
              ),
            ],
          ]);
        },
      ),
    );
  }
}

/// One image, loaded with the sign-in token (a plain network image cannot send it) and shown from memory.
class AttachmentTile extends StatefulWidget {
  const AttachmentTile({super.key, required this.api, required this.attachment});
  final ApiClient api;
  final Attachment attachment;

  @override
  State<AttachmentTile> createState() => _AttachmentTileState();
}

class _AttachmentTileState extends State<AttachmentTile> {
  late final Future<Uint8List> _bytes = widget.api.attachmentBytes(widget.attachment.id);

  @override
  Widget build(BuildContext context) {
    final item = widget.attachment;
    return SizedBox(
      width: 120,
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(
          height: 90,
          width: 120,
          child: FutureBuilder<Uint8List>(
            future: _bytes,
            builder: (context, snapshot) {
              if (snapshot.hasError) return const Center(child: Icon(Icons.broken_image_outlined));
              if (!snapshot.hasData) return const Center(child: CircularProgressIndicator(strokeWidth: 2));
              return InkWell(
                onTap: () => showDialog<void>(
                  context: context,
                  builder: (_) => Dialog(child: InteractiveViewer(child: Image.memory(snapshot.data!, errorBuilder: (_, _, _) => const Icon(Icons.broken_image_outlined)))),
                ),
                child: Image.memory(snapshot.data!, fit: BoxFit.cover, errorBuilder: (_, _, _) => const Icon(Icons.broken_image_outlined)),
              );
            },
          ),
        ),
        Text(item.fileName, maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.bodySmall),
        Text('${item.uploader}${item.internal ? ' · internal' : ''}', maxLines: 1, overflow: TextOverflow.ellipsis, style: Theme.of(context).textTheme.bodySmall),
      ]),
    );
  }
}
