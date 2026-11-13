import 'package:flutter/material.dart';

import 'api.dart';
import 'pages.dart';
import 'strings.dart';

void main() => runApp(HelpdeskApp(api: ApiClient()));

class HelpdeskApp extends StatefulWidget {
  const HelpdeskApp({super.key, required this.api, this.pickImage = pickImageFile, this.language});

  final ApiClient api;

  /// How the person chooses an image to attach; replaced in tests.
  final PickImage pickImage;

  /// The language to start in; by default Vietnamese on a phone set to Vietnamese, English otherwise.
  final String? language;

  @override
  State<HelpdeskApp> createState() => _HelpdeskAppState();
}

class _HelpdeskAppState extends State<HelpdeskApp> {
  final _navigator = GlobalKey<NavigatorState>();
  late final LanguageController _language;
  bool _signedIn = false;

  @override
  void initState() {
    super.initState();
    _language = LanguageController(
      widget.language ?? LanguageController.fromDevice(WidgetsBinding.instance.platformDispatcher.locale),
    );
    // the server words its own messages in the language chosen here
    widget.api.language = _language.language;
    _language.addListener(() => widget.api.language = _language.language);
    // the light or dark choice changes the theme of the whole app, which this widget builds
    _language.addListener(() => setState(() {}));
    // an expired session sends the person back to the sign-in page, wherever they were
    widget.api.onSignedOut = () => setState(() => _signedIn = false);
  }

  @override
  void dispose() {
    _language.dispose();
    super.dispose();
  }

  void _changed(bool signedIn) {
    if (!signedIn) {
      widget.api.signOut();
      _navigator.currentState?.popUntil((route) => route.isFirst);
    }
    setState(() => _signedIn = signedIn);
  }

  @override
  Widget build(BuildContext context) {
    return StringsScope(
      controller: _language,
      child: MaterialApp(
        title: 'Helpdesk',
        navigatorKey: _navigator,
        theme: ThemeData(colorSchemeSeed: const Color(0xFF3B5BDB), useMaterial3: true),
        darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF3B5BDB), brightness: Brightness.dark, useMaterial3: true),
        themeMode: _language.themeMode,
        home: _signedIn
            ? TicketsPage(api: widget.api, pickImage: widget.pickImage, onSignOut: () => _changed(false))
            : LoginPage(api: widget.api, onSignedIn: () => _changed(true)),
      ),
    );
  }
}
