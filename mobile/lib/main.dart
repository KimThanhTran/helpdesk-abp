import 'package:flutter/material.dart';

import 'api.dart';
import 'pages.dart';

void main() => runApp(HelpdeskApp(api: ApiClient()));

class HelpdeskApp extends StatefulWidget {
  const HelpdeskApp({super.key, required this.api, this.pickImage = pickImageFile});

  final ApiClient api;

  /// How the person chooses an image to attach; replaced in tests.
  final PickImage pickImage;

  @override
  State<HelpdeskApp> createState() => _HelpdeskAppState();
}

class _HelpdeskAppState extends State<HelpdeskApp> {
  final _navigator = GlobalKey<NavigatorState>();
  bool _signedIn = false;

  @override
  void initState() {
    super.initState();
    // an expired session sends the person back to the sign-in page, wherever they were
    widget.api.onSignedOut = () => setState(() => _signedIn = false);
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
    return MaterialApp(
      title: 'Helpdesk',
      navigatorKey: _navigator,
      theme: ThemeData(colorSchemeSeed: const Color(0xFF3B5BDB), useMaterial3: true),
      darkTheme: ThemeData(colorSchemeSeed: const Color(0xFF3B5BDB), brightness: Brightness.dark, useMaterial3: true),
      home: _signedIn
          ? TicketsPage(api: widget.api, pickImage: widget.pickImage, onSignOut: () => _changed(false))
          : LoginPage(api: widget.api, onSignedIn: () => _changed(true)),
    );
  }
}
