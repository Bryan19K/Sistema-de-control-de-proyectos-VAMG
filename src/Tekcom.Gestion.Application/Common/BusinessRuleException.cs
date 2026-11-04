namespace Tekcom.Gestion.Application.Common;

/// <summary>
/// Se lanza cuando una operación incumple una regla de negocio (por ejemplo, una entrega mayor al
/// stock). El mensaje está en español y se muestra tal cual al usuario.
/// </summary>
public sealed class BusinessRuleException(string message) : Exception(message);
