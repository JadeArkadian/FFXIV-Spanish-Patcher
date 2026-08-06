# Firma Windows de las releases

Las releases de Windows se firman con el certificado autofirmado público en
`docs/security/FFXIVSpanishPatcher-self-signed.pem`. No es un certificado de confianza pública:
Windows puede seguir mostrando advertencias. La comprobación principal para usuarios es el fichero
`*.zip.sha256` adjunto a cada release.

La clave privada no se versiona. Está en el directorio local ignorado `artifacts/self-signing/` y debe
guardarse fuera del repositorio. Para habilitar la firma en GitHub Actions, crea estos secrets de
repositorio:

- `WINDOWS_SELF_SIGNING_PFX_BASE64`: contenido de
  `artifacts/self-signing/WINDOWS_SELF_SIGNING_PFX_BASE64.txt`.
- `WINDOWS_SELF_SIGNING_PFX_PASSWORD`: contenido de
  `artifacts/self-signing/FFXIVSpanishPatcher-self-signing-password.txt`.

El workflow compara el certificado extraído del PFX con el certificado público versionado. Si no
coinciden, la release falla antes de publicar el ejecutable Windows.

El ejecutable se publica de forma nativa en `windows-latest` y se transfiere como artifact interno a
un job `ubuntu-latest`, donde `osslsigncode` lo firma y verifica sin modificar almacenes de confianza
de Windows. Solo después se empaqueta y se sube a GitHub Releases y Nexus Mods.
