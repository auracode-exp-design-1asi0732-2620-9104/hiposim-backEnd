# language: es
@EP05 @EP06
Característica: Registro de Comprador
  Como Comprador
  Quiero registrarme con mi correo electrónico y una contraseña
  Para acceder a las funciones de guardado, comparación y exportación

  @US13-E1 @TS07-E1 @pending
  Escenario: Registro exitoso
    Dado que el Comprador ingresa nombre, correo, celular y contraseña válidos
    Y acepta los Términos y la autorización de tratamiento de datos personales (Ley N° 29733)
    Cuando envía el registro
    Entonces el sistema crea la cuenta y responde con código 201
    Y emite un token JWT de sesión

  @US13-E2 @TS07-E2 @pending
  Escenario: Correo ya registrado
    Dado que el correo ingresado ya tiene una cuenta
    Cuando envía el registro
    Entonces el sistema rechaza la solicitud con código 409
    Y informa que el correo ya está registrado

  @US13-E3 @pending
  Escenario: Términos no aceptados
    Dado que el Comprador no acepta los Términos
    Cuando envía el registro
    Entonces el sistema no crea la cuenta