<?php
/**
 * 译文 Yiwen 诊断信息接收端（宝塔 / PHP 部署版）
 *
 * 作用：接收软件「导出并上传诊断信息」按钮发来的 zip 包（POST body 为二进制 zip，
 *       非 multipart 表单），存到 uploads/ 目录，返回 XXX-XXX 格式查询码，开发者凭码找回。
 *
 * 客户端协议（DiagnosticUploadService.cs）：
 *   POST {endpoint}  Content-Type: application/zip，body = zip 二进制
 *   Header: x-overtranslate-version / x-overtranslate-os（可选）
 *   成功响应：HTTP 200 + JSON {"code":"ABC-123"}（两段 3 位，排除 I/L/O/U）
 *
 * 部署（宝塔面板）：
 *   1. 本文件放到网站目录，如 C:/wwwroot/update.baimoushare.cn/YiWen/diag/index.php
 *   2. 同目录建 uploads/ 文件夹（755，PHP 可写）
 *   3. 站点 PHP 版本 ≥ 7.0；无需伪静态
 *   4. 验证：curl -X POST --data-binary @test.zip -H "Content-Type: application/zip" \
 *        https://update.baimoushare.cn/yiwen/diag/
 *
 * 安全说明：
 *   - 查询码为 XXX-XXX 随机格式（与客户端 CodePattern 一致），不可猜测
 *   - 只收 zip（校验 PK 魔数），限制 5MB（与客户端 MaxUploadBytes 一致）
 *   - 上传 30 天后惰性清理（有人访问时扫一遍，无需 cron）
 */

$uploadDir = __DIR__ . '/uploads';

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    header('Content-Type: application/json');
    echo json_encode(['ok' => false, 'error' => 'method not allowed']);
    exit;
}

// ---------- 读取原始请求体（客户端把 zip 二进制整个放进 body） ----------
$raw = file_get_contents('php://input');
$size = strlen($raw);

if ($size === 0) {
    http_response_code(400);
    header('Content-Type: application/json');
    echo json_encode(['ok' => false, 'error' => 'empty body']);
    exit;
}

if ($size > 5 * 1024 * 1024) {
    http_response_code(413);
    header('Content-Type: application/json');
    echo json_encode(['ok' => false, 'error' => 'file too large']);
    exit;
}

// zip 魔数：PK\x03\x04（或空 zip PK\x05\x06）
if (substr($raw, 0, 2) !== 'PK') {
    http_response_code(415);
    header('Content-Type: application/json');
    echo json_encode(['ok' => false, 'error' => 'not a zip']);
    exit;
}

// ---------- 落盘：生成客户端可识别的查询码 XXX-XXX ----------
// 与客户端 CodePattern ^[0-9A-HJKMNP-TV-Z]{3}-[0-9A-HJKMNP-TV-Z]{3}$ 完全一致
// 字符集：数字 0-9 + 大写字母去掉 I、L、O、U（避免手抄混淆）
if (!is_dir($uploadDir)) {
    mkdir($uploadDir, 0755, true);
}

$chars = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
$code = '';
for ($i = 0; $i < 6; $i++) {
    $code .= $chars[random_int(0, strlen($chars) - 1)];
}
$code = substr($code, 0, 3) . '-' . substr($code, 3, 3);

if (file_put_contents($uploadDir . '/' . $code . '.zip', $raw) === false) {
    http_response_code(500);
    header('Content-Type: application/json');
    echo json_encode(['ok' => false, 'error' => 'save failed']);
    exit;
}

// ---------- 惰性清理：删除 30 天前的旧包 ----------
$now = time();
foreach (glob($uploadDir . '/*.zip') as $old) {
    if ($now - filemtime($old) > 30 * 86400) {
        @unlink($old);
    }
}

// ---------- 响应（客户端只认 JSON 里的 code 字段） ----------
header('Content-Type: application/json');
echo json_encode(['code' => $code]);
