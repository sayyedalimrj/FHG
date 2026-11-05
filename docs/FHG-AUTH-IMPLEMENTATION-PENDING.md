# FHG authentication integration

The existing signed upstream Authenticator extension must not be modified in place: signed XPI content changes invalidate Mozilla verification. A native-messaging companion must be separately signed before bundled Firefox can install it.
