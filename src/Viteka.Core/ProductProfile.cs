namespace Viteka.Core;

// Set at build time. A missing/edited configuration file can never turn a test build into production.
public static class ProductProfile
{
#if VITEKA_REVIEW
    public static bool IsReview => true;
    public const string StateFolder="UtilidadesReviewAccess";
    public const string UserFolder="UtilidadesPruebas";
    public const string ServiceName="VitekaUtilidadesReviewAccess";
    public const string ShellAppId="Viteka.Utilidades.Pruebas";
    public const int AccessPort=7448;
#else
    public static bool IsReview => false;
    public const string StateFolder="UtilidadesAccess";
    public const string UserFolder="Utilidades";
    public const string ServiceName="VitekaUtilidadesAccess";
    public const string ShellAppId="Viteka.Utilidades";
    public const int AccessPort=7447;
#endif
}
