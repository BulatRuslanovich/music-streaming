package com.caimack.app;

import android.os.Bundle;
import androidx.activity.OnBackPressedCallback;
import com.getcapacitor.BridgeActivity;

/**
 * Аппаратная «назад» без обработки закрывает приложение с первого нажатия: у Capacitor нет
 * встроенной привязки её к истории WebView. Для интерфейса с вложенной навигацией — из плейлиста
 * в альбом, оттуда в артиста — это означало бы вылет вместо шага назад, поэтому кнопка сначала
 * отматывает историю и только на её конце отдаёт управление системе.
 *
 * Обработка живёт здесь, а не во фронте: приложение открывает удалённый сайт, и складывать в его
 * бандл код, который нужен только оболочке, значило бы тащить нативную специфику в браузерную сборку.
 */
public class MainActivity extends BridgeActivity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        getOnBackPressedDispatcher()
            .addCallback(
                this,
                new OnBackPressedCallback(true) {
                    @Override
                    public void handleOnBackPressed() {
                        if (getBridge() != null && getBridge().getWebView().canGoBack()) {
                            getBridge().getWebView().goBack();
                            return;
                        }

                        // Истории больше нет: отключаем себя и повторяем нажатие, чтобы сработало
                        // штатное поведение системы — свернуть или закрыть приложение.
                        setEnabled(false);
                        getOnBackPressedDispatcher().onBackPressed();
                        setEnabled(true);
                    }
                }
            );
    }
}
