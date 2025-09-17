using System.ComponentModel.DataAnnotations;

namespace GenReports.Models
{
    /// <summary>
    /// Modelo para el request body que contiene datos de usuarios
    /// </summary>
    public class UserDataRequest
    {
        /// <summary>
        /// Lista de datos de usuarios
        /// </summary>
        [Required]
        public List<UserData> Data { get; set; } = new List<UserData>();
    }

    /// <summary>
    /// Modelo que representa los datos de un usuario
    /// </summary>
    public class UserData
    {
        /// <summary>
        /// Usuario de la aplicación
        /// </summary>
        public string? AppUser { get; set; }

        /// <summary>
        /// Número de cédula de identificación
        /// </summary>
        public long? IdentificactionCard { get; set; }

        /// <summary>
        /// Nombre completo del usuario
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Código de zona
        /// </summary>
        public string? Zone { get; set; }

        /// <summary>
        /// Descripción de la zona
        /// </summary>
        public string? ZoneDescription { get; set; }

        /// <summary>
        /// Código de dependencia
        /// </summary>
        public string? Dependency { get; set; }

        /// <summary>
        /// Descripción de la dependencia
        /// </summary>
        public string? DependencyDescription { get; set; }

        /// <summary>
        /// Código de oficina
        /// </summary>
        public string? Office { get; set; }

        /// <summary>
        /// Descripción de la oficina
        /// </summary>
        public string? OfficeDescription { get; set; }

        /// <summary>
        /// Código de rol
        /// </summary>
        public string? Role { get; set; }

        /// <summary>
        /// Descripción del rol
        /// </summary>
        public string? RoleDescription { get; set; }

        /// <summary>
        /// Correo electrónico
        /// </summary>
        public string? Mail { get; set; }

        /// <summary>
        /// Extensión telefónica
        /// </summary>
        public int? Extension { get; set; }

        /// <summary>
        /// Usuario supervisor
        /// </summary>
        public string? Supervisor { get; set; }

        /// <summary>
        /// Nombre del supervisor
        /// </summary>
        public string? SupervisorName { get; set; }

        /// <summary>
        /// Tipo de usuario
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Descripción del tipo
        /// </summary>
        public string? TypeDescription { get; set; }

        /// <summary>
        /// Máximo número de sesiones
        /// </summary>
        public int? MaximunSesssion { get; set; }

        /// <summary>
        /// Estado del usuario
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Indica si es técnico
        /// </summary>
        public string? Technician { get; set; }

        /// <summary>
        /// Impresora asignada
        /// </summary>
        public string? Printer { get; set; }

        /// <summary>
        /// Impresora auxiliar
        /// </summary>
        public string? AuxiliaryPrinter { get; set; }

        /// <summary>
        /// Número de celular
        /// </summary>
        public string? CellPhone { get; set; }

        /// <summary>
        /// Lugar de expedición de la cédula
        /// </summary>
        public string? IssuanceCedula { get; set; }

        /// <summary>
        /// Contraseña del usuario
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Fecha de desactivación
        /// </summary>
        public DateTime? DeactivationDate { get; set; }

        /// <summary>
        /// Foto del usuario
        /// </summary>
        public string? Photo { get; set; }

        /// <summary>
        /// Empresa donde trabaja
        /// </summary>
        public string? CompanyWork { get; set; }

        /// <summary>
        /// Nombre de la empresa
        /// </summary>
        public string? CompanyWorkName { get; set; }

        /// <summary>
        /// Indica si tiene perfil de auditoría
        /// </summary>
        public bool HasAuditProfile { get; set; }

        /// <summary>
        /// Estado en base de datos
        /// </summary>
        public string? DbStatus { get; set; }

        /// <summary>
        /// Estado de la cuenta
        /// </summary>
        public string? AccountStatus { get; set; }
    }
}