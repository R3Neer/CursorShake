# Trabajo en CursorShake

- Lenguaje elegido: C#. La fase actual es requisitos y diseño. No implementar ni iniciar pruebas técnicas sin autorización posterior del usuario.
- El usuario conduce el diseño; explicar y revisar sus propuestas antes de completar modelos o código por él.
- Consultar docs/Decisiones.md y los requisitos antes de trabajar. Mantener los acuerdos formales en documentos, no únicamente en el chat. Las propuestas pendientes no autorizan su adopción silenciosa.
- No hay CLI de usuario. El control previsto es bandeja, diálogos nativos y TOML. No convertir la ausencia de un editor de opciones en ausencia de interacción gráfica.
- Preservar datos y trabajo ajeno. La futura migración del TOML debe verificar el destino antes de retirar el original, sin sobrescribir archivos existentes ni alterar globalmente el perfil de Windows.
- Mantener las licencias de terceros separadas de la política MIT del trabajo propio; no relicenciar recursos GPL como MIT.
- No hacer commit ni push sin autorización. La autorización para el commit inicial de documentación del 21-oct-2026 se limita a esa entrega; no autoriza futuras publicaciones.
