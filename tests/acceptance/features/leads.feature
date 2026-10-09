# language: es
@EP08 @EP06
Característica: Conexión con inmobiliarias
  Como Comprador
  Quiero compartir mi simulación con inmobiliarias solo si yo lo autorizo
  Para recibir información de proyectos en mi rango presupuestal

  Como Asesor de inmobiliaria
  Quiero gestionar los interesados que llegan
  Para darles seguimiento

  @US21-E1 @TS08-E1 @pending
  Escenario: Envío autorizado
    Dado que el Comprador ha iniciado sesión y tiene una simulación completada
    Cuando autoriza de forma expresa compartir su simulación y su contacto
    Entonces el sistema registra la solicitud y la pone a disposición de las inmobiliarias
    Y responde con código 201 y el identificador del lead
    Y confirma el envío al Comprador

  @US21-E2 @pending
  Escenario: Sesión requerida
    Dado que el Comprador no ha iniciado sesión
    Cuando solicita enviar su simulación
    Entonces el sistema le pide registrarse o iniciar sesión antes de continuar
    Y no comparte ningún dato

  @US21-E3 @TS08-E2 @pending
  Escenario: Sin autorización
    Dado que el Comprador no otorga la autorización
    Cuando finaliza el proceso
    Entonces el sistema responde con código 400
    Y no registra el lead
    Y no comparte ninguno de sus datos

  @US23-E1 @TS08-E3 @pending
  Escenario: Bandeja de leads
    Dado que el Asesor ha iniciado sesión
    Cuando consulta su bandeja
    Entonces el sistema responde con código 200
    Y lista solo los leads de su inmobiliaria con nombre, inmueble simulado, cuota estimada, TCEA, estado y fecha de recepción

  @US23-E2 @pending
  Escenario: Filtro por estado
    Dado que el Asesor aplica un filtro por estado
    Cuando consulta su bandeja
    Entonces el sistema muestra únicamente los leads que se encuentran en ese estado

  @US23-E3 @pending
  Escenario: Solo leads autorizados
    Dado que un Comprador no autorizó compartir su simulación
    Cuando el Asesor consulta su bandeja
    Entonces el sistema no muestra a ese Comprador

  @US24-E1 @pending
  Escenario: Ficha del lead
    Dado que el Asesor selecciona un lead de su bandeja
    Cuando abre su ficha
    Entonces el sistema muestra los datos de contacto del Comprador
    Y la simulación compartida con valor del inmueble, cuota inicial, bono aplicado, monto financiado, plazo, cuota y TCEA
    Y el historial de seguimiento

  @US24-E3 @pending
  Escenario: Lead de otra inmobiliaria
    Dado que el lead pertenece a otra inmobiliaria
    Cuando el Asesor intenta acceder a su ficha
    Entonces el sistema deniega el acceso

  @US25-E1 @pending
  Esquema del escenario: Cambio de estado
    Dado que el Asesor abre la ficha de un lead
    Cuando cambia su estado a <estado>
    Entonces el sistema actualiza el estado
    Y registra la fecha y el responsable del cambio

    Ejemplos:
      | estado         |
      | Nuevo          |
      | En contacto    |
      | Cita agendada  |
      | Cerrado        |
      | Descartado     |

  @US25-E2 @pending
  Escenario: Nota de seguimiento
    Dado que el Asesor abre la ficha de un lead
    Cuando registra una nota
    Entonces el sistema la agrega al historial con su fecha y su autor