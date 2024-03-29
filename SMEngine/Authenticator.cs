﻿using System;
using System.Security.Cryptography;
using System.Text;


namespace SMEngine
{
    public static class Authenticator
    {
        static public string Encrypt(string password, int salt)
        {
            if (password != null)
            {
                var passwordBytes = Encoding.Unicode.GetBytes(password);
                var saltBytes = Encoding.Unicode.GetBytes(salt.ToString());
                //note: this locks decryption to CurrentUser on CurrentMachine!
                var cipherBytes = ProtectedData.Protect(passwordBytes, saltBytes, DataProtectionScope.CurrentUser);

                return Convert.ToBase64String(cipherBytes);
            }
            return null;
        }

        static public string Decrypt(string cipher, int salt)
        {
            if (string.IsNullOrEmpty(cipher))
                return null;


            var cipherBytes = Convert.FromBase64String(cipher);
            var saltBytes = Encoding.Unicode.GetBytes(salt.ToString());
            //note: this locks decryption to CurrentUser on CurrentMachine!
            var passwordBytes = ProtectedData.Unprotect(cipherBytes, saltBytes, DataProtectionScope.CurrentUser);

            return Encoding.Unicode.GetString(passwordBytes);

        }
    }

}
