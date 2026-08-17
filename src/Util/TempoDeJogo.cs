namespace Mochila.Util
{
    /// <summary>
    /// Como o tempo jogado aparece na tela. A biblioteca guarda segundos; ninguém quer
    /// ler "12840 s jogados".
    /// </summary>
    public static class TempoDeJogo
    {
        public static string Descrever(int segundos)
        {
            if (segundos <= 0) return "nunca jogado";

            // Sessão curta existe e precisa aparecer — era exatamente o que o campo em
            // minutos apagava.
            if (segundos < 60) return $"{segundos} s jogados";

            var minutos = segundos / 60;
            if (minutos < 60) return $"{minutos} min jogados";

            return $"{minutos / 60} h {minutos % 60} min jogados";
        }

        /// <summary>Forma curta, para caber no card ou numa coluna estreita.</summary>
        public static string DescreverCurto(int segundos)
        {
            if (segundos <= 0) return "—";
            if (segundos < 60) return $"{segundos}s";

            var minutos = segundos / 60;
            return minutos < 60 ? $"{minutos}min" : $"{minutos / 60}h{minutos % 60:00}";
        }
    }
}
