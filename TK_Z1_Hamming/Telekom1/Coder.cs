using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Animation;

namespace Telekom1
{
    class Coder
    {
        //macierz korekcyjna
        private byte[][] H =
        {
            new byte[] {1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0},
            new byte[] {0, 1, 1, 1, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0},
            new byte[] {1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0},
            new byte[] {1, 1, 1, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0},
            new byte[] {0, 1, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0},
            new byte[] {1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0},
            new byte[] {1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0},
            new byte[] {1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1}
        };

        private byte[] T; // 16 bitów, 8 znak + 8 bity parzystości
        private int[] control; // 8 bitów wynik mnozenia macierzy

        public Coder()
        {
            // inicjalizacja tablic
            T = new byte[16];
            control = new int[8];
        }

        // mnożenie macierzy (T*H), wynik w control
        private void multiplyMatrix()
        {
            for (int i = 0; i < H.Length; i++)
            {
                for (int j = 0; j < T.Length; j++)
                    
                    control[i] += T[j] * H[i][j];

                control[i] %= 2;
            }
        }

        // zamienia znak ASCII na 8 bitw zapisanzch w Stringu
        public String ASCII2bin(byte c)
        {
            String tmp = Convert.ToString(c, 2);

            if (tmp.Length > 8)
               tmp = tmp.Substring(tmp.Length - 8, tmp.Length);

            while (tmp.Length < 8)
            {
                tmp = "0" + tmp;
            }

            return tmp;
        }

        // zamienia 8 bitw zapisanzch w Stringu na znak ASCII
        public byte bin2ASCII(String bin)
        {
            var padLeft = bin.PadLeft(8, '0');
            int result=0;
            Int32.TryParse(padLeft, out result);
            return (byte)result;
        }

        // ładuje String do tablicy T
        private void loadT(String bin)
        {
            byte[] b = Encoding.UTF8.GetBytes(bin);
            for (int i = 0; i < b.Length; i++)
            {
                b[i] -= 48; // 48 = ASCII '0'
                T[i] = b[i];
            }
        }

        // zamienia znak ASCII na jego reprezentacje 16 bitową
        private void encodeChar(char c)
        {
            loadT(ASCII2bin((byte) c));
            multiplyMatrix();

            // dopisz control do T
            for (int i = 8; i < T.Length; i++)
                T[i] = (byte)control[i - 8];
        }

        // oblicza wektor błędu dla 16 bitów
        private void decodeChar(String bin)
        {
            loadT(bin);
            multiplyMatrix();
        }

        // sprawdza czy wystąpił błąd
        private bool isCorrect()
        {
            for (int i = 0; i < control.Length; i++)
            {
                if (control[i] == 1)
                    return false;
            }

            return true;
        }

        // szuka 1 błędu, zwraca -1 gdy jest ich więcej
        private int findError()
        {
            bool isFound;

            for (int i = 0; i < T.Length; i++)
            {
                isFound = true;
                for (int j = 0; j < control.Length; j++)
                    if (H[j][i] != (byte)control[j])
                    {
                        isFound = false;
                        break;
                    }
                if (isFound)
                    return i;
            }

            return -1;
        }

        // szuka 2 błędy, zwraca -1 w indexes[0] gdy jest ich więcej
        private int[] findErrors()
        {
            bool isFound;
            int[] indexes = new int[2]; // zawiera pozycje 2 błędów

            // podobnie jak wyżej, ale do tego sprawdza każdy z każdym
            for (int i = 0; i < T.Length - 1; i++)
            {
                for (int j = i + 1; j < T.Length; j++)
                {
                    isFound = true;
                    for (int k = 0; k < control.Length; k++)
                    {
                        if (((H[k][i] + H[k][j]) % 2) != control[k])
                        {
                            isFound = false;
                            break;
                        }
                    }
                    if (isFound)
                    {
                        indexes[0] = i;
                        indexes[1] = j;
                        return indexes;
                    }
                }
            }
            indexes[0] = -1;
            indexes[1] = -1;
            return indexes;
        }

        // naprawia 1 błąd, gdy jest ich więcej przekierowuje dalej
        private bool repairError()
        {
            int index = findError();

            if (index == -1)
            {
                return repairErrors();
            }

            // zmienia 0 -> 1 lub 1 -> 0
            T[index] += 1;
            T[index] %= 2;

            return true;
        }

        // naprawia 2 błędy, gdy jest ich więcej zwraca false
        private bool repairErrors()
        {
            int[] indexes = findErrors();

            if (indexes[0] == -1 && indexes[1] == -1)
            {
                return false;
            }

            T[indexes[0]] += 1;
            T[indexes[0]] %= 2;

            T[indexes[1]] += 1;
            T[indexes[1]] %= 2;

            return true;
        }

        // koduje plik ze znakami ASCII na ich 16 bitową reprezentacje
        public String encodeString(String input)
        {
            String output = "";

            // usuń zbędne nowe linie
            input = input.Replace(" ", "");
            input = input.Replace("\n", "");

            // podziel String na pojedyncze znaki
            char[] chars = input.ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                // zamienia znak i ładuje do T
                encodeChar(chars[i]);

                for (int j = 0; j < T.Length; j++)
                {

                    if (j == 8) output += " ";

                    // zapisz 16 bitów do pliku
                    output += T[j];
                }
                output += " \n";
            }

            // wyczyszczenie tablic
            control = new int[8];
            T = new byte[16];

            return output;
        }

        // dekoduje plik z 16 bitową reprezentacją ASCII, wykrywając max 2 błędy
        public String decodeString(String input)
        {
            String output = "";


            // usuń zbędne spacje i nowe linie
            input = input.Replace(" ", "");
            input = input.Replace("\n", "");

            String[] bins = new String[input.Length / 16];

            // podziel całość na 16 znakowe Stringi
            for (int i = 0, j = 0; i < input.Length; i += 16, j++)
            {
                bins[j] = input.Substring(i, 16);
                if (bins[j].ToCharArray(0, 1)[0] == '1')
                {
                    bins[j] = "0" + input.Substring(i + 1, 16);
                }
            }

            for (int i = 0; i < bins.Length; i++)
            {
                // ładuje 16 bitów do T i liczy wektor błędu
                decodeChar(bins[i]);

                // wykryto błąd- natępuje próba jego naprawienia
                if (!isCorrect())
                    if (!repairError())
                        throw new Exception();

                String ascii = "";

                // złączenie 8 bitów znaku i zamiana na char
                for (int j = 0; j < 8; j++)
                {
                    ascii += T[j];
                }
                output +=Convert.ToChar(Convert.ToByte(ascii,2));

                // wyczyszczenie tablicy control
                control = new int[16];

            }

            // wyczyszczenie tablicy T
            T= new byte[16];

            return output;
        }
    }
}