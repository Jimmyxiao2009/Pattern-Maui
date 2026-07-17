declare module 'mailparser' {
  export function simpleParser(source: unknown): Promise<{
    from?: {text?: string};
    subject?: string;
    text?: string;
    html?: string;
  }>;
}

declare module 'nodemailer' {
  export function createTransport(options: unknown): {sendMail(message: unknown): Promise<unknown>};
}
