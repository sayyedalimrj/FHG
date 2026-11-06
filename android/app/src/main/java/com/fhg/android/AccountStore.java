package com.fhg.android;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Account secrets are encrypted using a non-exportable Android Keystore key.
 * Never print email/password/TOTP/subscription in logs. No cloud backup.
 * Imported four-line files stay under the user's control (SAF); the app stores
 * only AES-256-GCM ciphertext in internal no-backup private storage.
 */
final class AccountStore {
    private static final String ALIAS = "fhg-android-v1-account-key";
    private static final byte VERSION = 1;
    private final Context context;
    private final AtomicFile encrypted;
    private final SharedPreferences prefs;

    static final class Account {
        final String id;
        final String email;
        final String password;
        final String totpSecret;
        final String subscription;

        Account(String id, String email, String password, String totpSecret, String subscription) {
            this.id = id;
            this.email = email;
            this.password = password;
            this.totpSecret = totpSecret;
            this.subscription = subscription;
        }

        JSONObject toJson() throws Exception {
            return new JSONObject().put("id", id).put("email", email)
                    .put("password", password).put("totp", totpSecret)
                    .put("subscription", subscription);
        }

        static Account fromJson(JSONObject item) throws Exception {
            String id = item.getString("id");
            if (!id.matches("[0-9a-fA-F\\-]{36}")) throw new Exception("Invalid stored ID");
            return new Account(id, item.getString("email"),
                    item.getString("password"), item.getString("totp"),
                    item.getString("subscription"));
        }
    }

    AccountStore(Context context) {
        this.context = context.getApplicationContext();
        this.encrypted = new AtomicFile(new File(this.context.getNoBackupFilesDir(), "fhg-accounts.aesgcm"));
        this.prefs = this.context.getSharedPreferences("fhg-public-state", Context.MODE_PRIVATE);
    }

    private SecretKey key() throws Exception {
        KeyStore keys = KeyStore.getInstance("AndroidKeyStore");
        keys.load(null);
        KeyStore.Entry existing = keys.getEntry(ALIAS, null);
        if (existing instanceof KeyStore.SecretKeyEntry)
            return ((KeyStore.SecretKeyEntry) existing).getSecretKey();
        KeyGenerator generator = KeyGenerator.getInstance("AES", "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256).build());
        return generator.generateKey();
    }

    private byte[] seal(byte[] plaintext) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key(), new SecureRandom());
        byte[] iv = cipher.getIV();
        byte[] encryptedBytes = cipher.doFinal(plaintext);
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        output.write(VERSION);
        output.write(iv.length);
        output.write(iv);
        output.write(encryptedBytes);
        return output.toByteArray();
    }

    private byte[] unseal(byte[] data) throws Exception {
        if (data.length < 32 || data[0] != VERSION || data[1] != 12)
            throw new IllegalStateException("Encrypted vault has an unsupported format");
        byte[] iv = java.util.Arrays.copyOfRange(data, 2, 14);
        byte[] ciphertext = java.util.Arrays.copyOfRange(data, 14, data.length);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, iv));
        return cipher.doFinal(ciphertext);
    }

    synchronized List<Account> load() throws Exception {
        if (!encrypted.getBaseFile().exists()) return new ArrayList<>();
        byte[] plaintext = unseal(encrypted.readFully()); // throws if corrupt, never silently resets
        JSONArray array = new JSONArray(new String(plaintext, StandardCharsets.UTF_8));
        ArrayList<Account> accounts = new ArrayList<>();
        for (int i = 0; i < array.length(); ++i)
            accounts.add(Account.fromJson(array.getJSONObject(i)));
        java.util.Arrays.fill(plaintext, (byte) 0);
        return accounts;
    }

    synchronized void save(List<Account> accounts) throws Exception {
        JSONArray array = new JSONArray();
        for (Account item : accounts) array.put(item.toJson());
        byte[] plaintext = array.toString().getBytes(StandardCharsets.UTF_8);
        byte[] bytes = seal(plaintext);
        java.util.Arrays.fill(plaintext, (byte) 0);
        java.io.FileOutputStream stream = null;
        try {
            stream = encrypted.startWrite();
            stream.write(bytes);
            encrypted.finishWrite(stream);
        } catch (Exception failure) {
            if (stream != null) encrypted.failWrite(stream);
            throw failure;
        }
    }

    synchronized Account importFourLines(InputStream stream) throws Exception {
        // Bounded read works on all supported Android API 26+ devices;
        // InputStream.readNBytes is absent on some older Android runtimes.
        ByteArrayOutputStream bounded = new ByteArrayOutputStream();
        byte[] block = new byte[2048];
        int countRead;
        while ((countRead = stream.read(block)) != -1) {
            if (bounded.size() + countRead > 16384)
                throw new Exception("Configuration exceeds 16 KiB");
            bounded.write(block, 0, countRead);
        }
        byte[] raw = bounded.toByteArray();
        String[] lines = new String(raw, StandardCharsets.UTF_8)
                .replace("\\r\\n", "\\n").split("\\n", -1);
        int count = lines.length;
        while (count > 0 && lines[count - 1].isEmpty()) --count;
        if (count != 4) throw new Exception("Expected exactly four lines");
        String email = lines[0].trim();
        String password = lines[1];  // preserve literal spaces and Unicode
        String totp = lines[2].trim();
        String sub = lines[3].trim();
        if (!email.matches("[^\\s@]{1,100}@[^\\s@]{1,100}"))
            throw new Exception("Email format is invalid");
        if (password.isEmpty() || password.length() > 2048)
            throw new Exception("Password is missing");
        if (!totp.isEmpty()) Totp.validate(totp);
        if (!sub.startsWith("https://") || sub.length() > 4096)
            throw new Exception("Hiddify subscription must be HTTPS");

        ArrayList<Account> accounts = new ArrayList<>(load());
        String id = UUID.randomUUID().toString();
        for (Account existing : accounts) {
            if (existing.email.equalsIgnoreCase(email)) {
                id = existing.id; // update existing account, preserve Gecko context
                accounts.remove(existing);
                break;
            }
        }
        Account newAccount = new Account(id, email, password, totp, sub);
        accounts.add(newAccount);
        save(accounts);
        select(id);
        return newAccount;
    }


    /**
     * Persist the generated sing-box tunnel config only inside a separate
     * Android Keystore-encrypted file; NEVER pass passwords through Intents,
     * logs, world-readable files or plain SharedPreferences.
     */
    synchronized void saveVpnConfig(String accountId, String config) throws Exception {
        if (!accountId.matches("[0-9a-fA-F\\-]{36}") ||
            config == null || config.length() > 2000000)
            throw new IllegalArgumentException("Invalid VPN config");
        if (load().stream().noneMatch(a -> a.id.equals(accountId)))
            throw new IllegalStateException("Unknown account");
        AtomicFile file = new AtomicFile(
            new File(context.getNoBackupFilesDir(), "vpn-" + accountId + ".aesgcm"));
        byte[] data = config.getBytes(StandardCharsets.UTF_8);
        byte[] payload = seal(data);
        java.util.Arrays.fill(data, (byte)0);
        java.io.FileOutputStream out = null;
        try {
            out = file.startWrite();
            out.write(payload);
            file.finishWrite(out);
        } catch (Exception error) {
            if (out != null) file.failWrite(out);
            throw error;
        }
    }

    synchronized String loadVpnConfig(String accountId) throws Exception {
        if (accountId == null || !accountId.matches("[0-9a-fA-F\\-]{36}"))
            throw new IllegalArgumentException("Invalid account ID");
        AtomicFile file = new AtomicFile(
            new File(context.getNoBackupFilesDir(), "vpn-" + accountId + ".aesgcm"));
        if (!file.getBaseFile().exists())
            throw new java.io.FileNotFoundException("VPN configuration not yet imported");
        byte[] decoded = unseal(file.readFully());
        try { return new String(decoded, StandardCharsets.UTF_8); }
        finally { java.util.Arrays.fill(decoded, (byte)0); }
    }

    synchronized void updatePassword(String id, String password) throws Exception {
        if (password == null || password.isEmpty()) return;
        ArrayList<Account> accounts = new ArrayList<>(load());
        for (int i = 0; i < accounts.size(); i++) {
            Account previous = accounts.get(i);
            if (previous.id.equals(id) && !previous.password.equals(password)) {
                accounts.set(i, new Account(id, previous.email, password,
                        previous.totpSecret, previous.subscription));
                save(accounts);
                return;
            }
        }
    }

    synchronized Account selected() throws Exception {
        String id = prefs.getString("active", "");
        List<Account> list = load();
        for (Account account : list) if (account.id.equals(id)) return account;
        if (!list.isEmpty()) {
            select(list.get(0).id);
            return list.get(0);
        }
        return null;
    }

    void select(String id) {
        // Non-secret selected UUID only; passwords stay inside Keystore envelope.
        prefs.edit().putString("active", id).apply();
    }
}
