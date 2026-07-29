# X11 Native Residuals

## XIM preedit spot location

The managed X11 policy now prefers `XIMPreeditPosition | XIMStatusNothing` when that style is reported as supported, and it converts the client-logical caret rectangle to the client-physical `XPoint` expected by `XNSpotLocation`.

Native application is intentionally not implemented through direct C# P/Invoke. Discovering styles requires variadic `XGetIMValues`, while `XIMPreeditPosition` creation and later spot updates require variadic `XVaCreateNestedList`, `XCreateIC`, and `XSetICValues`. A fixed managed declaration cannot safely represent their ABI-dependent argument lists, and the repository does not contain a native fixed-signature shim.

The remaining native closure is a small C shim with non-variadic exports that:

1. Reads `XNQueryInputStyle` and returns copied `XIMStyle` values.
2. Creates an XIC with `XNInputStyle`, `XNClientWindow`, `XNFocusWindow`, and an `XNPreeditAttributes` nested list containing `XNSpotLocation`.
3. Updates an existing XIC's `XNPreeditAttributes` with a new `XPoint`.

Until that shim exists, the host continues to create the known-safe `XIMPreeditNothing | XIMStatusNothing` XIC. Committed UTF-8 text remains fully functional and is dispatched atomically.
