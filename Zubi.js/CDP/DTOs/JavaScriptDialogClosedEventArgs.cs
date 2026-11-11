namespace CDP.DTOs
{
    public class JavaScriptDialogClosedEventArgs
    {
        /// <summary>
        /// true si el usuario (o tu código) aceptó el diálogo; false si lo canceló.
        /// </summary>
        public bool Result { get; set; }

        /// <summary>
        /// El texto introducido por el usuario en un diálogo de tipo 'prompt'.
        /// Será una cadena vacía en otros tipos de diálogo.
        /// </summary>
        public string UserInput { get; set; } = "";
    }
}